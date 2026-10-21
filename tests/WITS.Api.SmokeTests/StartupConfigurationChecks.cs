using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WITS.Api.Auth;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class StartupConfigurationChecks
{
    internal const string SigningKey = "StartupSmokeTestsOnly-32ByteKey!!!";

    public static async Task RunAsync()
    {
        foreach (var field in new[] { "Issuer", "Audience", "SecurityKey" })
        {
            foreach (var value in new string?[] { null, "", "   " })
            {
                await ExpectRejectedAsync($"JwtSettings:{field}", value);
            }
        }
        await ExpectRejectedAsync("JwtSettings:SecurityKey", new string('x', 31));
        await ExpectRejectedAsync("JwtSettings:SecurityKey", new string('\u00e9', 15));
        await ExpectRejectedAsync("JwtSettings:SecurityKey", "YourLongSecretKeyHere_Replace_In_Production");
        await ExpectRejectedAsync("JwtSettings:SecurityKey", "YourLongSecretKeyHere_Replace_In_Production", "Staging");
        await ExpectRejectedAsync("JwtSettings:ExpirationMinutes", "0");
        await ExpectRejectedAsync("JwtSettings:ExpirationMinutes", "-1");
        await ExpectRejectedAsync("JwtSettings:ExpirationMinutes", "not-a-number");
        await ExpectRejectedAsync("ConnectionStrings:DefaultConnection", null);
        await ExpectRejectedAsync("ConnectionStrings:DefaultConnection", "Data Source=relative.db");
        await ExpectRejectedAsync("ConnectionStrings:DefaultConnection", "not-a-connection-string");

        var temporary = Path.Combine(Path.GetTempPath(), "opencode", $"wits-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporary);
        try
        {
            var database = Path.Combine(temporary, "existing.db");
            var connectionString = new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString();
            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version = 17; CREATE TABLE Sentinel (Value TEXT); INSERT INTO Sentinel VALUES ('keep');";
                await command.ExecuteNonQueryAsync();
            }
            await using (var application = CreateApplication(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            }))
            {
                using var client = application.CreateClient();
            }
            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version";
                Require((long)(await command.ExecuteScalarAsync())! == 17, "Startup modified database metadata.");
                command.CommandText = "SELECT Value FROM Sentinel";
                Require((string)(await command.ExecuteScalarAsync())! == "keep", "Startup modified existing data.");
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table'";
                Require((long)(await command.ExecuteScalarAsync())! == 1, "Startup left a probe table behind.");
            }

            var readOnly = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly };
            await ExpectRejectedAsync("ConnectionStrings:DefaultConnection", readOnly.ToString());
            var missing = new SqliteConnectionStringBuilder(connectionString)
            {
                DataSource = Path.Combine(temporary, "missing.db"), Mode = SqliteOpenMode.ReadWrite
            };
            await ExpectRejectedAsync("ConnectionStrings:DefaultConnection", missing.ToString());
            var invalid = Path.Combine(temporary, "invalid.db");
            await File.WriteAllTextAsync(invalid, new string('x', 4096));
            await ExpectRejectedAsync("ConnectionStrings:DefaultConnection",
                new SqliteConnectionStringBuilder { DataSource = invalid, Pooling = false }.ToString());
            await ExpectRejectedAsync("ConnectionStrings:DefaultConnection",
                new SqliteConnectionStringBuilder { DataSource = Path.Combine(invalid, "child.db") }.ToString());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(temporary, recursive: true);
        }

        foreach (var key in new[] { new string('x', 32), new string('\u00e9', 16) })
        {
            await using var application = CreateApplication(new Dictionary<string, string?> { ["JwtSettings:SecurityKey"] = key });
            using var client = application.CreateClient();
            using var scope = application.Services.CreateScope();
            var token = scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(new UserTokenModel
            {
                Id = 1,
                Username = "smoke", EmailAddress = "smoke@example.com", OrganisationId = 1
            });
            var parameters = application.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters.Clone();
            Require(parameters.ValidIssuer == "smoke-issuer" && parameters.ValidAudience == "smoke-audience",
                "Bearer authentication did not use the configured issuer/audience.");
            var handler = new JwtSecurityTokenHandler();
            _ = handler.ValidateToken(token, parameters, out var validated);
            Require(validated.ValidTo > DateTime.UtcNow, "The issued token has no positive lifetime.");
            parameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            _ = handler.ValidateToken(token, parameters, out _);
        }
        Console.WriteLine("PASS: startup rejects invalid JWT/database settings; SQLite probe preserves data; UTF-8 token signing validates.");
    }

    private static WebApplicationFactory<Routes> CreateApplication(Dictionary<string, string?> overrides, string environment = "Production")
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
            ["JwtSettings:Issuer"] = "smoke-issuer",
            ["JwtSettings:Audience"] = "smoke-audience",
            ["JwtSettings:SecurityKey"] = SigningKey,
            ["JwtSettings:ExpirationMinutes"] = "1"
        };
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }
        return new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
        });
    }

    private static async Task ExpectRejectedAsync(string setting, string? value, string environment = "Production")
    {
        await using var application = CreateApplication(new Dictionary<string, string?> { [setting] = value }, environment);
        try
        {
            using var client = application.CreateClient();
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException or ArgumentException or IOException or SqliteException)
        {
            Require(exception.ToString().Contains(setting, StringComparison.Ordinal), $"Startup error did not identify {setting}: {exception.Message}");
            return;
        }
        throw new InvalidOperationException($"Startup accepted invalid {setting} configuration in {environment}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
