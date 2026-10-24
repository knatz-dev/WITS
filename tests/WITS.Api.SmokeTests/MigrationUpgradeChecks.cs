using Dapper;
using Microsoft.Data.Sqlite;
using WITS.Common;
using WITS.Data.Entity;
using WITS.Migrator.Migrations;

using MigrationRunner = WITS.Migrator.Migrator;

namespace WITS.Api.SmokeTests;

internal static class MigrationUpgradeChecks
{
    public static async Task RunAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencode", $"wits-upgrades-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await CheckBaselineProgressionAsync(directory);
            await CheckUnsupportedHistoryAsync(directory);
            await CheckForwardRecoveryAsync(directory);
            await CheckVerifiedRollbackAsync(directory);
            Console.WriteLine("PASS: fresh-baseline progression preserves data/constraints/history, legacy or changed history is refused, unsafe rollback is blocked, and verified rollback/retry is atomic.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task CheckBaselineProgressionAsync(string directory)
    {
        var path = Path.Combine(directory, "baseline.db");
        var connectionString = ConnectionString(path);
        var migrations = MigrationRunner.GetMigrations().ToList();
        foreach (var migration in migrations.Take(migrations.Count - 1))
        {
            migration.Apply(connectionString);
        }
        string[] priorHistory;
        var (hash, salt) = AuthHelper.HashPassword("upgrade-fixture-password");
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                PRAGMA foreign_keys = ON;
                INSERT INTO Organisation (Id, Code, Name, Created) VALUES (7, 'UPGRADE', 'Upgrade fixture', '2026-10-24T00:00:00Z');
                INSERT INTO User (Id, OrganisationId, FirstName, LastName, Username, EmailAddress, PasswordHash, PasswordSalt, Created)
                    VALUES (3000000001, 7, 'Upgrade', 'User', 'upgrade-user', 'upgrade@example.test', @Hash, @Salt, '2026-10-24T00:00:00Z');
                INSERT INTO Projects (Id, OrganizationId, Name, Description, CreatedByUserId, Created)
                    VALUES (3000000002, 7, 'Upgrade project', 'Keep this project', 3000000001, '2026-10-24T00:00:00Z');
                INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (3000000002, 3000000001, 'Member');
                INSERT INTO OrganizationMembers (OrganizationId, UserId, Role) VALUES (7, 3000000001, 'Member');
                INSERT INTO Tickets (Id, ProjectId, Title, Description, TypeId, PriorityId, StatusId, CreatedByUserId, AssignedToUserId, Created)
                    VALUES (3000000003, 3000000002, 'Keep this ticket', 'Keep this description', 1, 1, 1, 3000000001, 3000000001, '2026-10-24T00:00:00Z');
                INSERT INTO TicketHistory (TicketId, ChangedByUserId, FieldName, NewValue) VALUES (3000000003, 3000000001, 'Title', 'Keep this ticket');
                """, new { Hash = hash, Salt = salt });
            priorHistory = (await connection.QueryAsync<string>("SELECT MigrationId || ':' || AppliedAt || ':' || hex(Script) FROM __Migrations ORDER BY MigrationId")).ToArray();
        }

        var runner = new MigrationRunner(connectionString);
        Require(runner.Migrate() == 0, "Baseline forward progression must succeed.");
        Require(runner.Migrate() == 0, "Repeating baseline progression must succeed.");
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            var user = await connection.QuerySingleAsync<User>("SELECT * FROM User WHERE Id = 3000000001");
            Require(user.OrganisationId == 7 && user.PasswordHash == hash && user.PasswordSalt == salt
                && AuthHelper.VerifyPasswordHash("upgrade-fixture-password", user.PasswordHash, user.PasswordSalt),
                "Forward migration must preserve account IDs, organisation, and working credentials.");
            Require(await connection.ExecuteScalarAsync<int>("""
                SELECT COUNT(*) FROM Tickets t
                JOIN Projects p ON p.Id = t.ProjectId
                JOIN ProjectMembers pm ON pm.ProjectId = p.Id AND pm.UserId = t.AssignedToUserId
                JOIN OrganizationMembers om ON om.OrganizationId = p.OrganizationId AND om.UserId = t.CreatedByUserId
                JOIN TicketHistory h ON h.TicketId = t.Id AND h.ChangedByUserId = t.CreatedByUserId
                WHERE t.Id = 3000000003 AND p.Id = 3000000002 AND t.CreatedByUserId = 3000000001
                    AND p.OrganizationId = 7 AND t.Title = 'Keep this ticket' AND t.Description = 'Keep this description'
                    AND p.Description = 'Keep this project' AND h.NewValue = t.Title AND t.RowVersion = 1 AND p.RowVersion = 1
                    AND t.Created = '2026-10-24T00:00:00Z' AND t.IsDeleted = 0
                """) == 1, "Forward migration must retain ticket/project data, long IDs, membership, history, and timestamps.");
            Require(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0, "Forward progression must keep foreign keys valid.");
            var history = (await connection.QueryAsync<string>("SELECT MigrationId || ':' || AppliedAt || ':' || hex(Script) FROM __Migrations ORDER BY MigrationId")).ToArray();
            Require(history.Length == migrations.Count && history.Take(priorHistory.Length).SequenceEqual(priorHistory), "Prior history records must remain unchanged.");
            RequireSqliteFailure(() => connection.Execute("""
                INSERT INTO User (OrganisationId, FirstName, LastName, Username, EmailAddress, Created)
                    VALUES (7, 'Duplicate', 'User', 'UPGRADE-USER', 'different@example.test', CURRENT_TIMESTAMP);
                """), 19);
            RequireSqliteFailure(() => connection.Execute("INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (3000000002, 999, 'Member');"), 19);
            RequireSqliteFailure(() => connection.Execute("UPDATE Tickets SET Title = NULL WHERE Id = 3000000003;"), 19);
        }

        var original = await File.ReadAllBytesAsync(path);
        RequireRejected(() => migrations[0].Rollback(connectionString), "latest");
        RequireRejected(() => migrations[^1].Rollback(connectionString), "forward-only");
        await RequireCliFailureAsync(directory, connectionString, ["rollback", migrations[0].MigrationId], "latest");
        await RequireCliFailureAsync(directory, connectionString, ["rollback", migrations[^1].MigrationId], "forward-only");
        var after = await File.ReadAllBytesAsync(path);
        Require(original.SequenceEqual(after), "Rejected rollback must preserve the complete database byte-for-byte.");

        // Line endings differ across Windows/Linux source checkouts, but the SQL is unchanged.
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            foreach (var migration in migrations)
            {
                await connection.ExecuteAsync("UPDATE __Migrations SET Script = @Script WHERE MigrationId = @Id",
                    new { Script = migration.UpScript.ReplaceLineEndings("\r\n"), Id = migration.MigrationId });
            }
        }
        Require(runner.Migrate() == 0, "Equivalent SQL line endings must not reject a supported baseline.");
    }

    private static async Task CheckUnsupportedHistoryAsync(string directory)
    {
        var mutations = new[]
        {
            ("changed-script", "UPDATE __Migrations SET Script = Script || '-- historical edit' WHERE MigrationId = '20250419113604';", "immutable"),
            ("changed-name", "UPDATE __Migrations SET Name = 'LegacySystem' WHERE MigrationId = '20250419113604';", "immutable"),
            ("gap", "DELETE FROM __Migrations WHERE MigrationId = '20250419113613';", "prefix"),
            ("unknown", "INSERT INTO __Migrations (MigrationId, Name, AppliedAt, Script) VALUES ('29990101000000', 'Unknown', CURRENT_TIMESTAMP, 'SELECT 1;');", "prefix")
        };
        foreach (var (name, mutation, message) in mutations)
        {
            var path = Path.Combine(directory, $"{name}.db");
            var connectionString = ConnectionString(path);
            var migrations = MigrationRunner.GetMigrations().ToList();
            // Test both a current database and rejection before a pending migration can write.
            var appliedCount = name == "changed-script" ? migrations.Count : migrations.Count - 1;
            foreach (var migration in migrations.Take(appliedCount))
            {
                migration.Apply(connectionString);
            }
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await connection.ExecuteAsync(mutation);
            }
            var original = await File.ReadAllBytesAsync(path);
            RequireRejected(() => new MigrationRunner(connectionString).Migrate(), message);
            if (message == "immutable")
            {
                RequireRejected(() => migrations[0].Apply(connectionString), message);
                RequireRejected(() => migrations[0].Rollback(connectionString), message);
            }
            await RequireCliFailureAsync(directory, connectionString, ["migrate"], message);
            var after = await File.ReadAllBytesAsync(path);
            Require(original.SequenceEqual(after), $"Rejecting {name} must not rewrite history or apply pending SQL.");
        }

        var legacyPath = Path.Combine(directory, "untracked-legacy.db");
        var legacy = ConnectionString(legacyPath);
        await using (var connection = new SqliteConnection(legacy))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE TABLE Users (Id INTEGER PRIMARY KEY, Email TEXT); INSERT INTO Users VALUES (42, 'preserve@example.test');");
        }
        var beforeLegacy = await File.ReadAllBytesAsync(legacyPath);
        RequireRejected(() => new MigrationRunner(legacy).Migrate(), "fresh");
        RequireRejected(() => new Migration20250419113604SystemSchema().Apply(legacy), "fresh");
        await RequireCliFailureAsync(directory, legacy, ["migrate"], "fresh");
        var afterLegacy = await File.ReadAllBytesAsync(legacyPath);
        Require(beforeLegacy.SequenceEqual(afterLegacy), "Untracked legacy data must not be initialized, rewritten, or guessed into the new baseline.");

        var emptyHistoryPath = Path.Combine(directory, "empty-history-legacy.db");
        var emptyHistory = ConnectionString(emptyHistoryPath);
        _ = new MigrationRunner(emptyHistory).GetAppliedMigrations();
        await using (var connection = new SqliteConnection(emptyHistory))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("CREATE TABLE Users (Id INTEGER PRIMARY KEY); INSERT INTO Users VALUES (42);");
        }
        var beforeEmpty = await File.ReadAllBytesAsync(emptyHistoryPath);
        RequireRejected(() => new MigrationRunner(emptyHistory).Migrate(), "fresh");
        RequireRejected(() => new Migration20250419113604SystemSchema().Apply(emptyHistory), "fresh");
        await RequireCliFailureAsync(directory, emptyHistory, ["migrate"], "fresh");
        var afterEmpty = await File.ReadAllBytesAsync(emptyHistoryPath);
        Require(beforeEmpty.SequenceEqual(afterEmpty), "An empty history table must not make an untracked application schema a supported baseline.");
    }

    private static async Task CheckForwardRecoveryAsync(string directory)
    {
        var path = Path.Combine(directory, "forward-retry.db");
        var connectionString = ConnectionString(path);
        var migrations = MigrationRunner.GetMigrations().ToList();
        foreach (var migration in migrations.Take(migrations.Count - 1))
        {
            migration.Apply(connectionString);
        }
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("""
                INSERT INTO User (Id, OrganisationId, FirstName, LastName, Username, EmailAddress, Created) VALUES
                    (41, 1, 'First', 'User', 'collision', 'first@example.test', CURRENT_TIMESTAMP),
                    (42, 1, 'Second', 'User', 'COLLISION', 'second@example.test', CURRENT_TIMESTAMP);
                """);
        }
        var before = await File.ReadAllBytesAsync(path);
        var runner = new MigrationRunner(connectionString);
        RequireSqliteFailure(() => runner.Migrate(), 19);
        var after = await File.ReadAllBytesAsync(path);
        Require(before.SequenceEqual(after), "Failed constraint migration must leave data, schema, and earlier history unchanged.");

        // Simulate an operator-approved data correction, without editing an applied migration or deleting history.
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await connection.ExecuteAsync("UPDATE User SET Username = 'second-user' WHERE Id = 42;");
        }
        Require(runner.Migrate() == 0, "Rerunning after a deliberate data correction must apply only the pending migration.");
        await using var verified = new SqliteConnection(connectionString);
        await verified.OpenAsync();
        Require(await verified.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM User WHERE Id IN (41, 42) AND RowVersion = 1") == 2
            && runner.GetAppliedMigrations().Count == migrations.Count, "Forward recovery must retain both accounts and complete history.");
    }

    private static async Task CheckVerifiedRollbackAsync(string directory)
    {
        var connectionString = ConnectionString(Path.Combine(directory, "verified-rollback.db"));
        var first = new ProbeMigration("001", "CREATE TABLE ProbeData (Id INTEGER PRIMARY KEY, Value TEXT); INSERT INTO ProbeData VALUES (42, 'preserve');", "DROP TABLE ProbeData;");
        var index = new ProbeMigration("002", "CREATE INDEX ProbeIndex ON ProbeData(Value);", "DROP INDEX ProbeIndex;", supportsRollback: true);
        first.Apply(connectionString);
        index.Apply(connectionString);
        RequireRejected(() => first.Rollback(connectionString), "latest");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("CREATE TRIGGER RejectHistoryDelete BEFORE DELETE ON __Migrations BEGIN SELECT RAISE(ABORT, 'history deletion failed'); END;");
        RequireSqliteFailure(() => index.Rollback(connectionString), 19);
        Require(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'ProbeIndex'") == 1
            && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM __Migrations") == 2, "Failed rollback must restore its DDL and keep the history entry.");
        await connection.ExecuteAsync("DROP TRIGGER RejectHistoryDelete;");
        index.Rollback(connectionString);
        index.Rollback(connectionString);
        Require(await connection.ExecuteScalarAsync<string>("SELECT Value FROM ProbeData WHERE Id = 42") == "preserve"
            && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'ProbeIndex'") == 0
            && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM __Migrations") == 1, "Verified latest rollback must restore prior schema, retain data, remove only its history, and repeat as a no-op.");
        index.Apply(connectionString);
        Require(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_schema WHERE name = 'ProbeIndex'") == 1, "A verified rollback must permit clean reapplication.");
    }

    private static async Task RequireCliFailureAsync(string directory, string connectionString, string[] command, string message)
    {
        var root = Path.GetFullPath("../../../../../", AppContext.BaseDirectory);
        var build = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.Name;
        var framework = new DirectoryInfo(AppContext.BaseDirectory).Name;
        var executable = Path.Combine(root, "tools", "WITS.Migrator", "bin", build, framework, "WITS.Migrator.dll");
        var result = await DatabaseTargetChecks.RunToolAsync(executable, directory, "UpgradeSmoke",
            [.. command, "--ConnectionStrings:DefaultConnection", connectionString]);
        Require(result.ExitCode == 1 && result.Output.Contains(message, StringComparison.OrdinalIgnoreCase),
            $"{command[0]} must fail with '{message}' guidance: {result.Output}");
    }

    private static void RequireRejected(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(message, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected migration refusal with '{message}' guidance.");
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
        throw new InvalidOperationException($"Expected SQLite failure {code}.");
    }

    private static string ConnectionString(string path) => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ProbeMigration(string id, string up, string down, bool supportsRollback = false) : Migration
    {
        public override string MigrationId => id;
        public override string Name => "Upgrade smoke probe";
        public override string UpScript => up;
        public override string DownScript => down;
        public override bool SupportsRollback => supportsRollback;
    }
}
