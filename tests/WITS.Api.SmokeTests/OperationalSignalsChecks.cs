using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
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

internal static class OperationalSignalsChecks
{
    public static async Task RunAsync()
    {
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OperationalSignals.MeterName
                || instrument.Name == "http.server.request.duration")
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Enqueue(new Measurement(instrument.Name, value, tags.ToArray().ToDictionary())));
        listener.Start();

        var (hash, salt) = AuthHelper.HashPassword("signals-secret");
        var account = new User
        {
            Id = 7, Username = "signals.user", FirstName = "Signals", LastName = "User",
            EmailAddress = "signals@example.com", OrganisationId = 3, PasswordHash = hash, PasswordSalt = salt
        };
        var logs = new DiagnosticsChecks.CapturingLoggerProvider();
        var directory = Path.Combine(Path.GetTempPath(), "opencode");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"wits-signals-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path, Pooling = false, DefaultTimeout = 1
        }.ToString();

        try
        {
            using var blocker = new SqliteConnection(connectionString);
            await blocker.OpenAsync();
            await blocker.ExecuteAsync("CREATE TABLE Probe (Id INTEGER);");
            using var transaction = await blocker.BeginTransactionAsync();
            await blocker.ExecuteAsync("INSERT INTO Probe VALUES (1)", transaction: transaction);

            await using var host = CreateHost(account, logs, connectionString);
            using var client = host.CreateClient();
            using (var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Username, "signals-secret")))
            {
                Require(response.StatusCode == HttpStatusCode.OK, "Successful login must still work.");
                Require(!measurements.Any(m => m.Name == "wits.auth.failures"), "Success must not count as authentication failure.");
            }

            await RejectedLoginAsync(client, account.Username, "wrong");
            await RejectedLoginAsync(client, "unknown", "wrong");
            using (var empty = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(account.Username, "")))
            {
                Require(empty.StatusCode == HttpStatusCode.BadRequest, "Empty password must be rejected at the boundary.");
            }
            account.IsLocked = true;
            await RejectedLoginAsync(client, account.Username, "signals-secret");
            account.IsLocked = false;

            using (var response = await client.GetAsync("/api/v1/ticket"))
            {
                Require(response.StatusCode == HttpStatusCode.Unauthorized, "Missing bearer must be rejected.");
            }
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
            using (var response = await client.GetAsync("/api/v1/ticket"))
            {
                Require(response.StatusCode == HttpStatusCode.Unauthorized, "Invalid bearer must be rejected.");
            }
            using var scope = host.Services.CreateScope();
            var token = scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(new UserTokenModel
            {
                Id = account.Id, Username = account.Username, EmailAddress = account.EmailAddress, OrganisationId = account.OrganisationId
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            foreach (var id in new[] { 42, 43, 44 })
            {
                using var response = await client.GetAsync($"/api/v1/ticket/{id}");
                Require(response.StatusCode == HttpStatusCode.InternalServerError, "SQLite failures must keep the existing 500 contract.");
                if (id == 42)
                {
                    using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    var traceId = problem.RootElement.GetProperty("traceId").GetString()!;
                    Require(logs.Entries.Any(log => log.Contains("Slow request", StringComparison.Ordinal)
                        && log.Contains(traceId, StringComparison.Ordinal) && log.Contains("{id:long}", StringComparison.Ordinal)),
                        "The real SQLite wait must produce a slow-request log with its route template and client-visible trace id.");
                }
            }
            foreach (var suffix in new[] { "unmatched-a", "unmatched-b" })
            {
                using var response = await client.GetAsync($"/{suffix}?secret=do-not-tag");
                Require(response.StatusCode == HttpStatusCode.NotFound, "Unmatched paths must answer 404.");
            }

            var samples = measurements.ToArray();
            foreach (var reason in new[] { "password_mismatch", "unknown_account", "account_locked", "missing_bearer", "invalid_bearer" })
            {
                Require(samples.Where(m => m.Name == "wits.auth.failures" && Equals(m.Tags["reason"], reason)).Sum(m => (long)m.Value) == 1,
                    $"Authentication reason {reason} must be counted exactly once.");
            }
            foreach (var error in new[] { "busy", "locked" })
            {
                Require(samples.Where(m => m.Name == "wits.sqlite.contention" && Equals(m.Tags["error.type"], error)).Sum(m => (long)m.Value) == 1,
                    $"SQLite {error} must be counted exactly once.");
            }
            Require(samples.Where(m => m.Name == "wits.sqlite.contention").Sum(m => (long)m.Value) == 2,
                "Other SQLite errors must not count as contention.");
            var requests = samples.Where(m => m.Name == "http.server.request.duration").ToArray();
            Require(requests.Length == 12 && requests.All(m => m.Value >= 0), "Each completed request must record a duration in seconds.");
            var counts = samples.Where(m => m.Name == "wits.http.requests").ToArray();
            Require(counts.Sum(m => (long)m.Value) == 12
                && counts.Where(m => Equals(m.Tags["http.response.status_code"], 500)).Sum(m => (long)m.Value) == 3
                && counts.Where(m => Equals(m.Tags["http.route"], "(unmatched)")).Sum(m => (long)m.Value) == 2,
                "Collected request counts must include handled errors and group unmatched paths together.");
            Require(requests.Count(m => Equals(m.Tags["http.response.status_code"], 500)) == 3,
                "Handled 500 responses must remain visible in the request metrics.");
            Require(requests.Any(m => m.Value >= 1), "The SQLite timeout must be visible in request latency.");
            Require(samples.SelectMany(m => m.Tags).All(tag =>
                !new[] { "signals.user", "signals-secret", "invalid-token", "do-not-tag", "unmatched-a", "unmatched-b", "/42", "/43", "/44" }
                    .Any(secret => (tag.Value?.ToString() ?? "").Contains(secret, StringComparison.Ordinal))),
                "Metrics must not label requests with credentials, raw paths, query strings, or resource IDs.");

            Console.WriteLine("PASS: request/error/latency metrics, auth failures, SQLite contention, and trace-correlated slow logs.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static WebApplicationFactory<Routes> CreateHost(User account, ILoggerProvider logs, string connectionString) =>
        new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(logs));
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
                services.Replace(ServiceDescriptor.Scoped<IUnitOfWork>(_ => new IdentityClaimsChecks.StubUnitOfWork(account)));
                services.Replace(ServiceDescriptor.Scoped<ITicketService>(_ => new ContendedTicketService(connectionString)));
            });
        });

    private static async Task RejectedLoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(username, password));
        Require(response.StatusCode == HttpStatusCode.Unauthorized, "Rejected credentials must answer 401.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Measurement(string Name, double Value, Dictionary<string, object?> Tags);

    private sealed class ContendedTicketService(string connectionString) : ITicketService
    {
        public async Task<TicketResponse?> GetByIdAsync(long id)
        {
            if (id == 43) throw new SqliteException("Simulated SQLITE_LOCKED", 6);
            using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync();
            await connection.ExecuteAsync(id == 42 ? "INSERT INTO Probe VALUES (2)" : "SELECT * FROM MissingTable", commandTimeout: 1);
            throw new InvalidOperationException("The fixture command should have failed.");
        }

        public Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize) => throw new NotSupportedException();
        public Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter) => throw new NotSupportedException();
    }
}
