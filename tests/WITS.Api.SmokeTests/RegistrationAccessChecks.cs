using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Dapper;
using Microsoft.Extensions.Configuration;
using WITS.Api.Auth;
using WITS.Data;
using WITS.Migrator.Migrations;

namespace WITS.Api.SmokeTests;

internal static class RegistrationAccessChecks
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", $"wits-registration-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

        try
        {
            new Migration20250419113604SystemSchema().Apply(connectionString);
            new Migration20250419113613AuthSchema().Apply(connectionString);
            new Migration20250419113619ProjectSchema().Apply(connectionString);
            new Migration20250419113732TicketSchema().Apply(connectionString);
            new Migration20250419115950OrganizationAndActivitySchema().Apply(connectionString);
            new Migration20261023090000ConstraintHardening().Apply(connectionString);
            new Migration20261107120000InvitationSchema().Apply(connectionString);

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
                await connection.ExecuteAsync("INSERT INTO Invitations (OrganisationId, Code, CreatedByUserId, ExpiresAt, Created, IsDeleted, IsRevoked) VALUES (1, 'REG-INVITE', 0, '2099-01-01T00:00:00Z', '2026-11-08T00:00:00Z', 0, 0);");
            }

            await using var application = new WebApplicationFactory<Routes>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, configuration) =>
                        configuration.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:DefaultConnection"] = connectionString
                        }));
                });

            using var client = application.CreateClient();

            var registerBody = """{"username":"reg.user","firstName":"Reg","lastName":"User","emailAddress":"reg@example.com","password":"password","telephoneNos":"000","organisationId":1,"invitationCode":"REG-INVITE"}""";
            using var registerResponse = await client.PostAsync("/api/v1/auth/register", new StringContent(registerBody, Encoding.UTF8, "application/json"));
            Require(registerResponse.StatusCode == HttpStatusCode.OK, "Eligible registration must succeed.");

            using var duplicateResponse = await client.PostAsync("/api/v1/auth/register", new StringContent(registerBody, Encoding.UTF8, "application/json"));
            Require(duplicateResponse.StatusCode == HttpStatusCode.Conflict, "Duplicate registration must conflict.");

            var loginBody = """{"username":"reg.user","password":"password"}""";
            using var loginResponse = await client.PostAsync("/api/v1/auth/login", new StringContent(loginBody, Encoding.UTF8, "application/json"));
            Require(loginResponse.StatusCode == HttpStatusCode.OK, "Login must succeed.");
            using var loginJson = JsonDocument.Parse(await loginResponse.Content.ReadAsStringAsync());
            var token = loginJson.RootElement.GetProperty("token").GetString();
            Require(!string.IsNullOrEmpty(token), "Login must return a token.");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ticket");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var ticketResponse = await client.SendAsync(request);
            Require(ticketResponse.StatusCode == HttpStatusCode.OK, "Protected resource access must succeed.");

            using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/ticket");
            invalidRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid");
            using var invalidResponse = await client.SendAsync(invalidRequest);
            Require(invalidResponse.StatusCode == HttpStatusCode.Unauthorized, "Invalid token must be rejected.");

            Console.WriteLine("PASS: real registration, login, and protected resource access work end to end.");
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(path + suffix); } catch { /* best effort */ }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}