using Microsoft.Extensions.Configuration;
using Spectre.Console;
using WITS.Migrator;

try
{
    // Load configuration from various sources
    var config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args)
        .Build();

    // Get database connection string from configuration
    var connectionString = config.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("DefaultConnection not found in configuration");

    // Ensure database directory exists
    var dbPath = Path.GetDirectoryName(connectionString.Split('=')[1]);
    if (!string.IsNullOrEmpty(dbPath) && !Directory.Exists(dbPath))
    {
        Directory.CreateDirectory(dbPath);
    }


    // Initialize the migrator
    Migrator migrator = new Migrator(connectionString);

    // Parse command-line arguments
    if (args.Length == 0)
    {
        Migrator.ShowHelp();
        return 0;
    }

    var command = args[0].ToLower();
    var parameters = args.Skip(1).ToArray();

    return command switch
    {
        "migrate" => Migrator.Migrate(),
        "rollback" => Migrator.Rollback(parameters),
        "list" => migrator.List(),
        "list-migrations" => migrator.ListMigrations(),
        "list-tables" => migrator.ListTables(),
        "remove" => migrator.Remove(parameters),
        "create" => Migrator.Create(parameters),
        _ => Migrator.ShowHelp()
    };
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex);
    return 1;
}
