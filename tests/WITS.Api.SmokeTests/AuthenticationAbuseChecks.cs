using System.Net;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WITS.Api.Auth;
using WITS.Common;
using WITS.Data;
using WITS.Data.Entity;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class AuthenticationAbuseChecks
{
    private const string Password = "abuse-check-password";

    public static async Task RunAsync()
    {
        var reasons = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalSignals.MeterName && instrument.Name == "wits.auth.failures")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "reason" && tag.Value is string reason) reasons.Add(reason);
            }
        });
        listener.Start();
        await CheckTrackerBoundsAsync();
        var (hash, salt) = AuthHelper.HashPassword(Password);
        var account = new User
        {
            Id = 7, Username = "abuse.user", FirstName = "Abuse", LastName = "Check",
            EmailAddress = "abuse@example.com", OrganisationId = 3, PasswordHash = hash, PasswordSalt = salt
        };
        await CheckLockoutRecoveryAsync(account);
        await CheckInfrastructureFailuresAsync(account);
        await CheckIpLimitsAsync(account);
        await CheckGlobalLimitAsync(account);
        await CheckConcurrencyLimitAsync(account);
        Require(new[] { "invalid_input", "login_throttled", "rate_limited" }.All(reasons.Contains),
            "Abuse-control rejections must emit their fixed, non-identifying operational reasons.");
        Console.WriteLine("PASS: generic login failures, bounded/atomic lockout with recovery, shared IP/global/concurrency auth limits, and 429 retry guidance.");
    }

    private static async Task CheckTrackerBoundsAsync()
    {
        var clock = new TestClock();
        var tracker = new LoginAttemptTracker(clock);
        var admissions = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => tracker.TryBegin(" Concurrent "))));
        Require(admissions.Count(allowed => allowed) == 1, "Only one concurrent attempt per normalized account may proceed.");
        tracker.Complete("concurrent", null);
        Require(tracker.TryBegin("CONCURRENT"), "Infrastructure failure must release the account reservation.");
        tracker.Complete("concurrent", true);

        for (var failure = 0; failure < LoginAttemptTracker.FailureLimit; failure++)
        {
            Require(tracker.TryBegin("case.user"), "The initial failure budget must be available.");
            tracker.Complete("case.user", false);
        }
        Require(!tracker.TryBegin(" CASE.USER "), "Case and whitespace must not bypass a live lockout.");
        clock.Advance(LoginAttemptTracker.Lockout - TimeSpan.FromSeconds(1));
        Require(!tracker.TryBegin("case.user"), "Lockout must last its full duration.");
        clock.Advance(TimeSpan.FromSeconds(1));
        Require(tracker.TryBegin("case.user"), "Rejected attempts must not extend lockout.");
        tracker.Complete("case.user", true);

        Require(tracker.TryBegin("in-flight"), "Reserve an in-flight attempt before filling the store.");
        for (var index = 0; index < 9_999; index++)
        {
            Require(tracker.TryBegin($"unknown-{index}"), "The bounded store should admit entries up to its capacity.");
            tracker.Complete($"unknown-{index}", false);
        }
        Require(!tracker.TryBegin("overflow"), "A full store must fail closed, not evict unexpired failures.");
        clock.Advance(LoginAttemptTracker.Lockout);
        Require(tracker.TryBegin("overflow"), "Expired records must free capacity for legitimate recovery.");
        Require(!tracker.TryBegin("in-flight"), "Cleanup must never evict an in-flight reservation.");
        tracker.Complete("overflow", true);
        tracker.Complete("in-flight", null);
    }

    private static async Task CheckLockoutRecoveryAsync(User account)
    {
        var clock = new TestClock();
        await using var host = CreateHost(account, clock);
        using var client = host.CreateClient();
        var source = 0;
        Task<HttpResponseMessage> Login(string username, string password) =>
            SendAsync(client, "login", new LoginRequest(username, password), $"192.0.2.{++source}");

        for (var failure = 0; failure < LoginAttemptTracker.FailureLimit; failure++)
        {
            using var response = await Login(account.Username, "wrong");
            await ExpectLoginFailureAsync(response);
        }
        using (var locked = await Login(account.Username, Password))
        {
            await ExpectLoginFailureAsync(locked);
        }
        for (var failure = 0; failure <= LoginAttemptTracker.FailureLimit; failure++)
        {
            using var unknown = await Login("unknown", "wrong");
            await ExpectLoginFailureAsync(unknown);
        }

        clock.Advance(LoginAttemptTracker.Lockout);
        using (var recovered = await Login(account.Username, Password))
        {
            Require(recovered.StatusCode == HttpStatusCode.OK, "Valid credentials must recover automatically after lockout.");
            using var body = JsonDocument.Parse(await recovered.Content.ReadAsStringAsync());
            Require(!string.IsNullOrEmpty(body.RootElement.GetProperty("token").GetString()), "Recovery must issue a token.");
        }
        // A successful login must clear the previous failure budget, repeatedly.
        for (var cycle = 0; cycle < 2; cycle++)
        {
            for (var failure = 0; failure < LoginAttemptTracker.FailureLimit - 1; failure++)
            {
                using var rejected = await Login(account.Username, "wrong");
                await ExpectLoginFailureAsync(rejected);
            }
            using var accepted = await Login(account.Username, Password);
            Require(accepted.StatusCode == HttpStatusCode.OK, "Success must reset failed attempts across scoped service instances.");
        }

        account.IsLocked = true;
        clock.Advance(LoginAttemptTracker.Lockout);
        using (var administrativelyLocked = await Login(account.Username, Password))
        {
            await ExpectLoginFailureAsync(administrativelyLocked);
            Require(account.IsLocked, "Timed recovery must not clear the existing administrative lock flag.");
        }
        account.IsLocked = false;
        using (var oversized = await Login(new string('x', 129), new string('x', 1025)))
        {
            Require(oversized.StatusCode == HttpStatusCode.BadRequest, "Invalid login fields must be rejected at the boundary with 400.");
        }
        using (var nullFields = await Login(null!, null!))
        {
            Require(nullFields.StatusCode == HttpStatusCode.BadRequest, "Null login fields must be rejected at the boundary with 400.");
        }
        var invalidRegistration = Registration();
        invalidRegistration.Password = "";
        using var invalid = await SendAsync(client, "register", invalidRegistration, "192.0.2.200");
        Require(invalid.StatusCode == HttpStatusCode.BadRequest, "Invalid registration credentials must fail before password hashing or insertion.");
    }

    private static async Task CheckIpLimitsAsync(User account)
    {
        var service = new CountingAuthService();
        await using var host = CreateHost(account, new TestClock(), service);
        using var client = host.CreateClient();
        using (var malformed = await client.PostAsync("/api/v1/auth/login", new StringContent("{invalid", Encoding.UTF8, "application/json")))
        {
            Require(malformed.StatusCode == HttpStatusCode.BadRequest, "Malformed requests must still count against the IP budget.");
        }
        for (var attempt = 1; attempt < 10; attempt++)
        {
            using var response = attempt % 2 == 0
                ? await SendAsync(client, "login", new LoginRequest("any", "any"), "192.0.2.1")
                : await SendAsync(client, "register", Registration(), "192.0.2.1");
            Require(response.StatusCode == HttpStatusCode.OK, "Login and registration must share the first ten IP permits.");
        }
        client.DefaultRequestHeaders.Add("Accept", "text/plain");
        using (var limited = await SendAsync(client, "login", new LoginRequest("changed-name", "any"), "::ffff:192.0.2.1"))
        {
            await ExpectRateLimitAsync(limited);
        }
        client.DefaultRequestHeaders.Remove("Accept");
        Require(service.Calls == 9, "IP rejection must happen before calling either authentication service operation.");
        using (var otherIp = await SendAsync(client, "register", Registration(), "192.0.2.2"))
        {
            Require(otherIp.StatusCode == HttpStatusCode.OK, "Another IP retains its own budget.");
        }
        using var unrelated = await client.GetAsync("/api/v1/ticket");
        Require(unrelated.StatusCode == HttpStatusCode.Unauthorized, "Authentication limits must not throttle unrelated API routes.");
    }

    private static async Task CheckGlobalLimitAsync(User account)
    {
        var service = new CountingAuthService();
        await using var host = CreateHost(account, new TestClock(), service);
        using var client = host.CreateClient();
        for (var attempt = 1; attempt <= 120; attempt++)
        {
            using var response = await SendAsync(client, "login", new LoginRequest("any", "any"), $"192.0.2.{attempt}");
            Require(response.StatusCode == HttpStatusCode.OK, "The process budget must admit its configured number of attempts.");
        }
        using var limited = await SendAsync(client, "register", Registration(), "192.0.2.200");
        await ExpectRateLimitAsync(limited);
        Require(service.Calls == 120, "Rotating IPs or switching endpoints must not bypass the process budget.");
    }

    private static async Task CheckInfrastructureFailuresAsync(User account)
    {
        await using var host = CreateHost(account, new TestClock()).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<IUnitOfWork, UnitOfWork>())));
        using var client = host.CreateClient();
        for (var attempt = 0; attempt <= LoginAttemptTracker.FailureLimit; attempt++)
        {
            using var response = await SendAsync(client, "login", new LoginRequest(account.Username, Password), "192.0.2.1");
            Require(response.StatusCode == HttpStatusCode.InternalServerError,
                "Missing schema must stay a database error, releasing the reservation without consuming credential failures.");
        }
    }

    private static async Task CheckConcurrencyLimitAsync(User account)
    {
        var service = new CountingAuthService { Block = true };
        await using var host = CreateHost(account, new TestClock(), service);
        using var client = host.CreateClient();
        var pending = Enumerable.Range(1, 4).Select(ip => SendAsync(client, "login", new LoginRequest("any", "any"), $"192.0.2.{ip}")).ToArray();
        try
        {
            await service.FourEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var limited = await SendAsync(client, "register", Registration(), "192.0.2.5");
            await ExpectRateLimitAsync(limited);
            Require(service.Calls == 4, "Only four authentication requests may execute concurrently, without a queue.");
        }
        finally
        {
            service.Release.TrySetResult();
            foreach (var response in await Task.WhenAll(pending)) response.Dispose();
        }
        using var recovered = await SendAsync(client, "register", Registration(), "192.0.2.6");
        Require(recovered.StatusCode == HttpStatusCode.OK, "Completion must release concurrency permits.");
    }

    private static WebApplicationFactory<Routes> CreateHost(User account, TestClock clock, IAuthService? service = null) =>
        new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["JwtSettings:Issuer"] = "smoke-issuer",
                ["JwtSettings:Audience"] = "smoke-audience",
                ["JwtSettings:SecurityKey"] = StartupConfigurationChecks.SigningKey,
                ["JwtSettings:ExpirationMinutes"] = "5"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Scoped<IUnitOfWork>(_ => new IdentityClaimsChecks.StubUnitOfWork(account)));
                services.AddSingleton<IStartupFilter, TestClientIpFilter>();
                if (service is not null) services.Replace(ServiceDescriptor.Singleton(service));
            });
        });

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string operation, object body, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/auth/{operation}") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Smoke-IP", ip);
        request.Headers.Add("X-Forwarded-For", "203.0.113.99"); // A valid spoofed address must not influence the production limiter.
        return await client.SendAsync(request);
    }

    private static async Task ExpectLoginFailureAsync(HttpResponseMessage response)
    {
        Require(response.StatusCode == HttpStatusCode.Unauthorized, "Rejected logins must remain generic 401s.");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Require(problem.RootElement.GetProperty("detail").GetString() == "Invalid credentials",
            "Wrong, unknown, locked, throttled and invalid credentials must not disclose account state.");
    }

    private static async Task ExpectRateLimitAsync(HttpResponseMessage response)
    {
        Require(response.StatusCode == HttpStatusCode.TooManyRequests
            && response.Content.Headers.ContentType?.MediaType == "application/problem+json"
            && response.Headers.RetryAfter?.Delta is { TotalSeconds: >= 1 and <= 60 },
            "Rate limiting must return a 429 problem with usable Retry-After guidance.");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Require(problem.RootElement.GetProperty("status").GetInt32() == 429
            && problem.RootElement.GetProperty("title").GetString() == "Too Many Requests"
            && !string.IsNullOrEmpty(problem.RootElement.GetProperty("detail").GetString()), "429 must use the shared problem shape.");
    }

    private static RegisterRequest Registration() => new()
    {
        Username = "new-user", Password = Password, FirstName = "New", LastName = "User", EmailAddress = "new@example.com",
        OrganisationId = 3, Created = DateTime.UtcNow, TelephoneNos = "000", Organisation = "Test"
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class TestClientIpFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers["X-Smoke-IP"].FirstOrDefault() ?? "192.0.2.1");
                return continuation(context);
            });
            next(app);
        };
    }

    private sealed class CountingAuthService : IAuthService
    {
        private int _calls;
        public int Calls => _calls;
        public bool Block { get; init; }
        public TaskCompletionSource FourEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string GenerateToken(UserTokenModel userToken) => throw new NotSupportedException();

        public Task<RefreshResult> RefreshAsync(string refreshToken) =>
            Task.FromResult(new RefreshResult(false, null, null, "Invalid refresh token"));

        public void RevokeRefreshToken(string refreshToken) { }
        public void RevokeAllRefreshTokens(long userId) { }

        public async Task<CredentialResult> ValidateCredentialsAsync(string username, string password)
        {
            if (Interlocked.Increment(ref _calls) == 4) FourEntered.TrySetResult();
            if (Block) await Release.Task;
            return new CredentialResult(true, "test-token");
        }

        public Task<RegistrationResult> RegisterUserAsync(RegisterRequest user)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new RegistrationResult(true, "Registered"));
        }
    }
}
