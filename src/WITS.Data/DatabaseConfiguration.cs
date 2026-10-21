using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using WITS.Data.Contracts;

namespace WITS.Data;

public static class DatabaseConfiguration
{
    public static ConfigurationManager LoadToolConfiguration(string[] args)
    {
        using var environmentConfiguration = new ConfigurationManager();
        environmentConfiguration
            .AddEnvironmentVariables("ASPNETCORE_")
            .AddEnvironmentVariables("DOTNET_")
            .AddCommandLine(args);
        var environment = environmentConfiguration["environment"] ?? "Production";

        var configuration = new ConfigurationManager();
        configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
        return configuration;
    }

    public static string GetConnectionString(IConfiguration configuration)
    {
        var value = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Set ConnectionStrings:DefaultConnection to an explicit SQLite database path for this environment.");
        }

        try
        {
            var connection = new SqliteConnectionStringBuilder(value);
            if (connection.DataSource == ":memory:" || connection.Mode == SqliteOpenMode.Memory)
            {
                return connection.ToString();
            }

            if (!Path.IsPathFullyQualified(connection.DataSource))
            {
                throw new InvalidOperationException("ConnectionStrings:DefaultConnection must use an absolute SQLite Data Source path so every process targets the same file.");
            }

            connection.DataSource = Path.GetFullPath(connection.DataSource);
            Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
            return connection.ToString();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is invalid or its database directory cannot be accessed.", exception);
        }
    }

    public static async Task ValidateAccessAsync(IDbConnectionFactory connectionFactory)
    {
        try
        {
            using var connection = await connectionFactory.CreateConnectionAsync();
            using var transaction = connection.BeginTransaction();
            // Force a real write (including journal access), then restore the original metadata.
            await connection.ExecuteAsync("PRAGMA user_version = 0", transaction: transaction);
            transaction.Rollback();
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection must provide read/write access to a valid SQLite database and its journal directory.", exception);
        }
    }
}
