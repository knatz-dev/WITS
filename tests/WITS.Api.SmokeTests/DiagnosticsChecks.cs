using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

internal static class DiagnosticsChecks
{
    private const string Password = "diagnostics-hunter2";

    public static async Task RunAsync()
    {
        var (hash, salt) = AuthHelper.HashPassword(Password);
        var account = new User
        {
            Id = 7,
            Username = "diag.user",
            FirstName = "Diag",
            LastName = "User",
            EmailAddress = "diag.user@example.com",
            OrganisationId = 3,
            PasswordHash = hash,
            PasswordSalt = salt
        };
        var provider = new CapturingLoggerProvider();

        await using var host = CreateHost(provider, account);
        using var client = host.CreateClient();

        using (var accepted = await client.PostAsync("/api/v1/auth/login",
                   Json(new LoginRequest(account.Username, Password))))
        {
            Require(accepted.StatusCode == HttpStatusCode.OK,
                $"Valid credentials must log in: {await accepted.Content.ReadAsStringAsync()}");
        }

        using (var rejected = await client.PostAsync("/api/v1/auth/login",
                   Json(new LoginRequest(account.Username, $"wrong-{Password}"))))
        {
            Require(rejected.StatusCode == HttpStatusCode.Unauthorized, "A wrong password must answer 401.");
        }

        var token = IssueToken(host, account.Id);
        using var failingRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ticket/1");
        failingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var failing = await client.SendAsync(failingRequest);
        var body = await failing.Content.ReadAsStringAsync();
        Require(failing.StatusCode == HttpStatusCode.InternalServerError,
            $"An unimplemented service must fail: {(int)failing.StatusCode} {body}");
        using var problem = JsonDocument.Parse(body);
        var traceId = problem.RootElement.GetProperty("traceId").GetString();

        var entries = provider.Entries.ToArray();
        Require(entries.Any(entry => entry.Contains("Login succeeded for diag.user", StringComparison.Ordinal)
            && entry.Contains("ms", StringComparison.Ordinal)),
            "A successful login must log its outcome and elapsed time.");
        Require(entries.Any(entry => entry.Contains("Login failed for diag.user", StringComparison.Ordinal)
            && entry.Contains("mismatch", StringComparison.Ordinal)),
            "A failed login must log its outcome and reason.");
        if (traceId is null || !entries.Any(entry => entry.Contains(traceId, StringComparison.Ordinal)
                && entry.Contains("Unhandled exception", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"The client-visible trace id must appear with the server-side error log. traceId={traceId}; body={body}; Captured entries:\n{string.Join('\n', entries)}");
        }
        Require(entries.All(entry => !entry.Contains(Password, StringComparison.Ordinal)),
            "No log entry may contain a password.");
        Require(entries.All(entry => !entry.Contains(token, StringComparison.Ordinal)),
            "No log entry may contain a token.");

        Console.WriteLine("PASS: operations log outcome, elapsed time, and trace-correlated failures without credentials or tokens.");
    }

    private static WebApplicationFactory<Routes> CreateHost(CapturingLoggerProvider provider, User account)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
            ["JwtSettings:Issuer"] = "smoke-issuer",
            ["JwtSettings:Audience"] = "smoke-audience",
            ["JwtSettings:SecurityKey"] = StartupConfigurationChecks.SigningKey,
            ["JwtSettings:ExpirationMinutes"] = "5"
        };
        return new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(provider));
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services => services.Replace(
                ServiceDescriptor.Scoped<IUnitOfWork>(_ => new IdentityClaimsChecks.StubUnitOfWork(account))));
        });
    }

    private static string IssueToken(WebApplicationFactory<Routes> host, long userId)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(new UserTokenModel
        {
            Id = userId,
            Username = "diag.user",
            EmailAddress = "diag.user@example.com",
            OrganisationId = 3
        });
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly LoggerExternalScopeProvider _scopes = new();

        public ConcurrentQueue<string> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() => GC.SuppressFinalize(this);

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider._scopes.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var scopes = new StringBuilder();
                provider._scopes.ForEachScope(static (scope, builder) => builder.Append('[').Append(scope).Append(']'), scopes);
                provider.Entries.Enqueue($"{logLevel}: {scopes}{formatter(state, exception)}");
            }
        }
    }
}
