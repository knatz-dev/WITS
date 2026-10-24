using Dapper;
using Microsoft.Data.Sqlite;
using WITS.Migrator.Migrations;

using MigrationRunner = WITS.Migrator.Migrator;

namespace WITS.Api.SmokeTests;

internal static class MigrationHistoryChecks
{
    private const string HistoryTable = """
        CREATE TABLE __Migrations (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            MigrationId TEXT NOT NULL UNIQUE,
            Name TEXT NOT NULL,
            AppliedAt DATETIME NOT NULL,
            Script TEXT NOT NULL);
        """;

    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", $"wits-history-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await CheckMalformedHistoryAsync(directory);
            foreach (var existingFile in new[] { false, true })
            {
                await CheckMigrationStatesAsync(directory, existingFile);
            }
            await CheckFailureAtomicityAsync(directory);
            await CheckInspectionFailuresAsync(directory);
            await CheckSharedMemoryAsync();
            Console.WriteLine("PASS: migration history bootstrap, partial/current states, malformed history and CLI failures, read-only/shared-memory inspection, and atomic failed migrations.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckMalformedHistoryAsync(string directory)
    {
        var fixtures = new[]
        {
            ("legacy", "CREATE TABLE __Migrations (LegacyId TEXT); INSERT INTO __Migrations VALUES ('keep-me');"),
            ("missing-script", "CREATE TABLE __Migrations (Id INTEGER PRIMARY KEY, MigrationId TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, AppliedAt DATETIME NOT NULL);"),
            ("not-unique", HistoryTable.Replace("TEXT NOT NULL UNIQUE", "TEXT NOT NULL", StringComparison.Ordinal)),
            ("nullable", HistoryTable.Replace("Script TEXT NOT NULL", "Script TEXT", StringComparison.Ordinal)),
            ("view", "CREATE VIEW __Migrations AS SELECT 'keep-me' AS MigrationId;"),
            ("bad-record", HistoryTable + "INSERT INTO __Migrations (MigrationId, Name, AppliedAt, Script) VALUES ('', 'Bad', 'not-a-date', '');")
        };
        var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var build = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.Name;
        var framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var executable = Path.Combine(root, "tools", "WITS.Migrator", "bin", build, framework, "WITS.Migrator.dll");
        foreach (var (name, script) in fixtures)
        {
            var path = Path.Combine(directory, $"{name}.db");
            var connectionString = ConnectionString(path);
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync(script + "CREATE TABLE ExistingData (Value TEXT); INSERT INTO ExistingData VALUES ('preserve');");
            }
            var original = await File.ReadAllBytesAsync(path);
            var runner = new MigrationRunner(connectionString);
            RequireInvalidHistory(() => runner.GetAppliedMigrations());
            var migration = new ProbeMigration("probe", "DROP TABLE ExistingData;");
            RequireInvalidHistory(() => migration.Apply(connectionString));
            RequireInvalidHistory(() => migration.Rollback(connectionString));
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                try
                {
                    _ = await MigrationRunner.GetMigrationStatus(connection);
                    throw new InvalidOperationException("Status inspection accepted incompatible history.");
                }
                catch (InvalidOperationException exception) when (exception.Message.Contains("__Migrations", StringComparison.Ordinal))
                {
                    // Both public inspection entrypoints must use the same validation.
                }
            }

            foreach (var command in new[] { new[] { "list-migrations" }, ["migrate"], ["rollback", "20250419113604"], ["remove", "all"] })
            {
                var result = await DatabaseTargetChecks.RunToolAsync(executable, directory, "HistorySmoke",
                    [.. command, "--ConnectionStrings:DefaultConnection", connectionString]);
                Require(result.ExitCode == 1 && result.Output.Contains("__Migrations", StringComparison.Ordinal)
                    && result.Output.Contains("repair", StringComparison.Ordinal),
                    $"{name}/{command[0]} must report actionable incompatible history and exit 1: {result.Output}");
            }
            var afterRejection = await File.ReadAllBytesAsync(path);
            Require(original.SequenceEqual(afterRejection),
                $"Rejecting {name} history must preserve all original schema, history, and application data.");
        }
    }

    private static async Task CheckMigrationStatesAsync(string directory, bool existingFile)
    {
        var path = existingFile ? Path.Combine(directory, "empty.db") : Path.Combine(directory, "new", "fresh.db");
        if (existingFile)
        {
            await File.WriteAllBytesAsync(path, []);
        }
        var connectionString = ConnectionString(path);
        var runner = new MigrationRunner(connectionString);
        Require(runner.GetAppliedMigrations().Count == 0, "A new/empty database must initialize empty history.");
        var migrations = MigrationRunner.GetMigrations().ToList();
        migrations[0].Apply(connectionString);
        Require(runner.GetAppliedMigrations().SetEquals([migrations[0].MigrationId]), "Partial history must retain exactly the applied migration.");
        Require(runner.Migrate() == 0 && runner.GetAppliedMigrations().SetEquals(migrations.Select(m => m.MigrationId)),
            "Migrate must complete a partial database and record every pending migration.");

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            // A current database must not reinsert or replace even one history record.
            await connection.ExecuteAsync("""
                CREATE TRIGGER RejectHistoryInsert BEFORE INSERT ON __Migrations BEGIN SELECT RAISE(ABORT, 'history insert on current database'); END;
                CREATE TRIGGER RejectHistoryUpdate BEFORE UPDATE ON __Migrations BEGIN SELECT RAISE(ABORT, 'history update on current database'); END;
                CREATE TRIGGER RejectHistoryDelete BEFORE DELETE ON __Migrations BEGIN SELECT RAISE(ABORT, 'history delete on current database'); END;
                """);
        }
        var original = await File.ReadAllBytesAsync(path);
        Require(runner.Migrate() == 0, "Migrating a current database must succeed without reapplying scripts.");
        foreach (var migration in migrations)
        {
            migration.Apply(connectionString);
        }
        var afterReapplication = await File.ReadAllBytesAsync(path);
        Require(original.SequenceEqual(afterReapplication), "Reapplication must not change schema, application data, or history.");
        var readOnly = new SqliteConnectionStringBuilder(connectionString) { Mode = SqliteOpenMode.ReadOnly }.ToString();
        Require(new MigrationRunner(readOnly).GetAppliedMigrations().Count == migrations.Count, "Valid history inspection must work without write permissions.");
    }

    private static async Task CheckFailureAtomicityAsync(string directory)
    {
        var connectionString = ConnectionString(Path.Combine(directory, "atomic.db"));
        new ProbeMigration("first", "CREATE TABLE ExistingData (Value TEXT); INSERT INTO ExistingData VALUES ('original');").Apply(connectionString);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        var runner = new MigrationRunner(connectionString);
        const string changes = "UPDATE ExistingData SET Value = 'changed'; CREATE TABLE MustRollback (Id INTEGER);";
        RequireSqliteFailure(() => new ProbeMigration("script-failure", changes + "INSERT INTO MissingTable VALUES (1);").Apply(connectionString), 1);
        await RequireRolledBackAsync(connection, runner);

        await connection.ExecuteAsync("CREATE TRIGGER RejectHistory BEFORE INSERT ON __Migrations BEGIN SELECT RAISE(ABORT, 'history write failed'); END;");
        RequireSqliteFailure(() => new ProbeMigration("history-failure", changes).Apply(connectionString), 19);
        await RequireRolledBackAsync(connection, runner);

        var fresh = ConnectionString(Path.Combine(directory, "failed-first.db"));
        RequireSqliteFailure(() => new ProbeMigration("first-failure", "CREATE TABLE MustRollback (Id INTEGER); INSERT INTO MissingTable VALUES (1);").Apply(fresh), 1);
        await using var freshConnection = new SqliteConnection(fresh);
        await freshConnection.OpenAsync();
        Require(await freshConnection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_schema WHERE name IN ('__Migrations', 'MustRollback')") == 0,
            "Failed first migration must roll back history initialization and its application schema together.");
    }

    private static async Task RequireRolledBackAsync(SqliteConnection connection, MigrationRunner runner)
    {
        Require(await connection.ExecuteScalarAsync<string>("SELECT Value FROM ExistingData") == "original", "Failed migration must restore preexisting data.");
        Require(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'MustRollback'") == 0, "Failed migration must roll back DDL.");
        Require(runner.GetAppliedMigrations().SetEquals(["first"]), "Failure must preserve previously applied history without adding the failed migration.");
    }

    private static async Task CheckInspectionFailuresAsync(string directory)
    {
        var missing = new SqliteConnectionStringBuilder(ConnectionString(Path.Combine(directory, "missing.db"))) { Mode = SqliteOpenMode.ReadWrite }.ToString();
        RequireSqliteFailure(() => new MigrationRunner(missing).GetAppliedMigrations(), 14);
        var corrupt = Path.Combine(directory, "corrupt.db");
        await File.WriteAllTextAsync(corrupt, "This is not a SQLite database.");
        RequireSqliteFailure(() => new MigrationRunner(ConnectionString(corrupt)).GetAppliedMigrations(), 26);

        var connectionString = ConnectionString(Path.Combine(directory, "locked.db"));
        _ = new MigrationRunner(connectionString).GetAppliedMigrations();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("BEGIN EXCLUSIVE;");
        try
        {
            RequireSqliteFailure(() => new MigrationRunner(connectionString).GetAppliedMigrations(), 5);
        }
        finally
        {
            await connection.ExecuteAsync("ROLLBACK;");
        }
    }

    private static async Task CheckSharedMemoryAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"history-{Guid.NewGuid():N}", Mode = SqliteOpenMode.Memory, Cache = SqliteCacheMode.Shared, Pooling = false
        }.ToString();
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        new ProbeMigration("memory", "CREATE TABLE MemoryData (Id INTEGER);").Apply(connectionString);
        Require(new MigrationRunner(connectionString).GetAppliedMigrations().SetEquals(["memory"]),
            "Inspection must read shared-memory history rather than treating a nonexistent disk file as no history.");
    }

    private static void RequireInvalidHistory(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("__Migrations", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("Malformed __Migrations was reported as empty history instead of rejected.");
    }

    private static void RequireSqliteFailure(Action action, int code)
    {
        try
        {
            action();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == code)
        {
            return;
        }
        throw new InvalidOperationException($"Expected original SQLite error {code} to propagate.");
    }

    private static string ConnectionString(string path) => new SqliteConnectionStringBuilder
    {
        DataSource = path, Pooling = false, DefaultTimeout = 1
    }.ToString();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class ProbeMigration(string id, string script) : Migration
    {
        public override string MigrationId => id;
        public override string Name => "History smoke probe";
        public override string UpScript => script;
        public override string DownScript => "DROP TABLE ExistingData;";
    }
}
