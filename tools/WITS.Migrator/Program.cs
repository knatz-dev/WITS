using Microsoft.Data.Sqlite;
using Spectre.Console;
using WITS.Data;
using WITS.Migrator;

try
{
    if (args.Length == 0)
    {
        Migrator.ShowHelp();
        return 0;
    }

    // Keep positional migration arguments out of the configuration provider.
    var optionIndex = Array.FindIndex(args, arg => arg.StartsWith("--", StringComparison.Ordinal));
    var commandArgs = optionIndex < 0 ? args : args[..optionIndex];
    var configurationArgs = optionIndex < 0 ? Array.Empty<string>() : args[optionIndex..];
    using var config = DatabaseConfiguration.LoadToolConfiguration(configurationArgs);
    var connectionString = DatabaseConfiguration.GetConnectionString(config);
    Console.WriteLine($"Database: {new SqliteConnectionStringBuilder(connectionString).DataSource}");
    Migrator migrator = new Migrator(connectionString);

    var command = commandArgs.FirstOrDefault()?.ToLowerInvariant();
    var parameters = commandArgs.Skip(1).ToArray();

    return command switch
    {
        "migrate" => migrator.Migrate(),
        "rollback" => migrator.Rollback(parameters),
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
