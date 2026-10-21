using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

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
}
