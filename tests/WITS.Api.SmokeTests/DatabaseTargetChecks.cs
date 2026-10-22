using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WITS.Api.Auth;
using WITS.Data;
using WITS.Data.Contracts;

namespace WITS.Api.SmokeTests;

internal static class DatabaseTargetChecks
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var build = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.Name;
        var framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var migrator = Path.Combine(root, "tools", "WITS.Migrator", "bin", build, framework, "WITS.Migrator.dll");
        var seeder = Path.Combine(root, "tools", "WITS.Seeder", "bin", build, framework, "WITS.Seeder.dll");
        var temporary = Path.Combine(Path.GetTempPath(), "opencode", $"wits-target-{Guid.NewGuid():N}");
        var environment = $"TargetSmoke{Guid.NewGuid():N}";
        var settingsPaths = new[]
        {
            Path.Combine(root, "src", "WITS.Api", $"appsettings.{environment}.json"),
            Path.Combine(Path.GetDirectoryName(migrator)!, $"appsettings.{environment}.json"),
            Path.Combine(Path.GetDirectoryName(seeder)!, $"appsettings.{environment}.json")
        };
        var previousConnection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Directory.CreateDirectory(temporary);
        try
        {
            CheckRejectedTarget(null);
            CheckRejectedTarget("Data Source=relative.db");
            CheckRejectedTarget("Data Source=");

            // A conflicting working-directory config must never redirect either tool.
            var decoy = Path.Combine(temporary, "wrong.db");
            await File.WriteAllTextAsync(Path.Combine(temporary, "appsettings.json"), Settings(decoy));
            var jsonTarget = Path.Combine(temporary, "environment.json.db");
            foreach (var path in settingsPaths)
            {
                await File.WriteAllTextAsync(path, Settings(jsonTarget));
            }

            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            await CheckApiAsync(environment, jsonTarget, []);
            await CheckToolAsync(migrator, temporary, environment, jsonTarget, ["list-tables"]);
            await CheckToolAsync(seeder, temporary, environment, jsonTarget, []);
            await CheckToolAsync(migrator, temporary, "WrongEnvironment", jsonTarget,
                ["list-tables", "--environment", environment]);
            await CheckToolAsync(seeder, temporary, "WrongEnvironment", jsonTarget,
                ["--environment", environment]);

            var envTarget = Path.Combine(temporary, "environment.variable.db");
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString(envTarget));
            await CheckApiAsync(environment, envTarget, []);
            await CheckToolAsync(migrator, temporary, environment, envTarget, ["list-tables"]);
            await CheckToolAsync(seeder, temporary, environment, envTarget, []);

            var cliTarget = Path.Combine(temporary, "command=target; with spaces.db");
            string[] options = ["--ConnectionStrings:DefaultConnection", ConnectionString(cliTarget)];
            await CheckApiAsync(environment, cliTarget, options);
            await CheckToolAsync(migrator, temporary, environment, cliTarget, ["list-tables", .. options]);
            await CheckToolAsync(seeder, temporary, environment, cliTarget, options);

            var migrationTarget = Path.Combine(temporary, "migration.db");
            var migrationResult = await RunToolAsync(migrator, temporary, environment,
                ["migrate", "--ConnectionStrings:DefaultConnection", ConnectionString(migrationTarget)]);
            Require(File.Exists(migrationTarget), $"Migration did not open the supplied target: {migrationResult.Output}");
            using (var connection = new SqliteConnection(ConnectionString(migrationTarget)))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = '__Migrations'";
                Require((long)(await command.ExecuteScalarAsync())! == 1, "Migration bootstrap used a different database.");
                // Apply currently fails at its existing COUNT cast; test targeting independently of that defect.
                command.CommandText = "INSERT OR IGNORE INTO __Migrations (MigrationId, Name, AppliedAt, Script) VALUES ('20250419113604', 'SystemSchema', CURRENT_TIMESTAMP, '')";
                await command.ExecuteNonQueryAsync();
            }

            var listing = await RunToolAsync(migrator, temporary, environment,
                ["list-migrations", "--ConnectionStrings:DefaultConnection", ConnectionString(migrationTarget)]);
            Require(listing.ExitCode == 0 && listing.Output.Contains("Applied", StringComparison.Ordinal),
                $"Inspection did not see the selected database's migration history: {listing.Output}");
            var rollback = await RunToolAsync(migrator, temporary, environment,
                ["rollback", "20250419113604", "--ConnectionStrings:DefaultConnection", ConnectionString(migrationTarget)]);
            Require(rollback.ExitCode == 0, $"Rollback failed: {rollback.Output}");
            using (var connection = new SqliteConnection(ConnectionString(migrationTarget)))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM __Migrations WHERE MigrationId = '20250419113604'";
                Require((long)(await command.ExecuteScalarAsync())! == 0, "Rollback used a different database.");
            }
            Require(!File.Exists(decoy), "A tool used the working-directory database instead of the explicit target.");
            await CheckMaintenanceOutcomesAsync(migrator, seeder, temporary, environment);
            Console.WriteLine("PASS: API/tool JSON, environment and CLI targets; migration bootstrap/inspection/rollback target isolation; maintenance-tool failure exit codes.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", previousConnection);
            SqliteConnection.ClearAllPools();
            foreach (var path in settingsPaths)
            {
                File.Delete(path);
            }
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static async Task CheckApiAsync(string environment, string target, string[] options)
    {
        await using var application = new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddCommandLine(options));
        });
        using var connection = await application.Services.GetRequiredService<IDbConnectionFactory>().CreateConnectionAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA database_list";
        using var reader = command.ExecuteReader();
        Require(reader.Read() && string.Equals(reader.GetString(2), target, StringComparison.OrdinalIgnoreCase),
            "API opened a different database from the selected target.");
    }

    private static async Task CheckToolAsync(string executable, string workingDirectory, string environment, string target, string[] args)
    {
        File.Delete(target);
        var result = await RunToolAsync(executable, workingDirectory, environment, args);
        Require(result.Output.Contains($"Database: {target}", StringComparison.Ordinal) && File.Exists(target),
            $"Tool did not open the selected target: {result.Output}");
        if (args.Contains("list-tables"))
        {
            Require(result.ExitCode == 0, $"Inspection failed: {result.Output}");
        }
        // Seeder data/transaction correctness is tracked separately; this checks its actual opened file.
    }

    private static async Task<(int ExitCode, string Output)> RunToolAsync(string executable, string workingDirectory, string environment, string[] args, bool keepStdinOpen = false)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "WrongEnvironment";
        start.ArgumentList.Add(executable);
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the database tool.");
        if (!keepStdinOpen)
        {
            process.StandardInput.Close();
        }
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        return (process.ExitCode, await output + await error);
    }

    private static async Task CheckMaintenanceOutcomesAsync(string migrator, string seeder, string temporary, string environment)
    {
        // Failures must exit nonzero, and tools must not wait for console input: stdin stays open here,
        // so any console pause would block until the run is killed instead of exiting with a code.
        var seedTarget = Path.Combine(temporary, "maintenance-seed.db");
        var seeding = await RunToolAsync(seeder, temporary, environment,
            ["--ConnectionStrings:DefaultConnection", ConnectionString(seedTarget)], keepStdinOpen: true);
        Require(seeding.ExitCode == 1 && seeding.Output.Contains("Seeding failed", StringComparison.Ordinal),
            $"Seeder failures must report the failure and exit nonzero: exit {seeding.ExitCode}, {seeding.Output}");

        // Migration removal must not report success when a file cannot be deleted.
        var sandbox = Path.Combine(temporary, "remove-sandbox");
        var migrationsDirectory = Path.Combine(sandbox, "Migrations");
        Directory.CreateDirectory(migrationsDirectory);
        var lockedFile = Path.Combine(migrationsDirectory, "20250419113613_AuthSchema.cs");
        var unlockedFile = Path.Combine(migrationsDirectory, "20250419115950_OrganizationAndActivitySchema.cs");
        await File.WriteAllTextAsync(lockedFile, "// sandbox migration file");
        await File.WriteAllTextAsync(unlockedFile, "// sandbox migration file");
        var removeTarget = Path.Combine(temporary, "maintenance-remove.db");
        string[] removeOptions = ["--ConnectionStrings:DefaultConnection", ConnectionString(removeTarget)];

        // Holding a file open blocks deletion only on Windows; elsewhere deletion simply succeeds.
        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var locked = await RunToolAsync(migrator, sandbox, environment,
                    ["remove", "20250419113613", .. removeOptions]);
                Require(locked.ExitCode == 1 && File.Exists(lockedFile),
                    $"A failed file deletion must exit nonzero and keep the file: exit {locked.ExitCode}, {locked.Output}");

                var all = await RunToolAsync(migrator, sandbox, environment,
                    ["remove", "all", .. removeOptions]);
                Require(all.ExitCode == 1 && File.Exists(lockedFile),
                    $"Removal of many must report failed deletions: exit {all.ExitCode}, {all.Output}");
            }
        }

        var removed = await RunToolAsync(migrator, sandbox, environment,
            ["remove", "20250419113613", .. removeOptions]);
        Require(removed.ExitCode == 0 && !File.Exists(lockedFile),
            $"Removal must exit zero and delete the file: exit {removed.ExitCode}, {removed.Output}");

        var unlocked = await RunToolAsync(migrator, sandbox, environment,
            ["remove", "20250419115950", .. removeOptions]);
        Require(unlocked.ExitCode == 0 && !File.Exists(unlockedFile),
            $"Removal must exit zero and delete the requested file: exit {unlocked.ExitCode}, {unlocked.Output}");
    }

    private static void CheckRejectedTarget(string? value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = value
        }).Build();
        try
        {
            DatabaseConfiguration.GetConnectionString(configuration);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Missing or relative database configuration should be rejected.");
    }

    private static string ConnectionString(string target) => new SqliteConnectionStringBuilder { DataSource = target, Pooling = false }.ToString();
    private static string Settings(string target) => JsonSerializer.Serialize(new
    {
        ConnectionStrings = new { DefaultConnection = ConnectionString(target) },
        JwtSettings = new { SecurityKey = StartupConfigurationChecks.SigningKey }
    });
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
