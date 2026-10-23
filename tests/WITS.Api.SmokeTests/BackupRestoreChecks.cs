using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WITS.Common;
using WITS.Data;
using WITS.Data.Factory;
using WITS.Migrator.Migrations;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class BackupRestoreChecks
{
    private const string Username = "backup-user";
    private const string Password = "backup-restore-password";
    private const string SigningKey = "BackupRestoreChecksSigningKey-32Bytes";

    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", $"wits-backup-{Guid.NewGuid():N}.db");
        var backupPath = $"{path}.backup";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        try
        {
            var migrations = ApplyMigrations(connectionString);

            // Take an online backup from a WAL database while the source connection stays open.
            await using (var source = new SqliteConnection(connectionString))
            {
                await source.OpenAsync();
                await source.ExecuteAsync("PRAGMA journal_mode = WAL;");
                await source.ExecuteAsync(
                    "INSERT INTO Organisation (Name, Code, Description, Created) VALUES ('Backup', 'BACKUP', 'Backup check', @Now);",
                    new { Now = DateTime.UtcNow });
                var organisationId = await source.ExecuteScalarAsync<long>("SELECT last_insert_rowid();");
                var (hash, salt) = AuthHelper.HashPassword(Password);
                await source.ExecuteAsync(
                    """
                    INSERT INTO User (OrganisationId, FirstName, LastName, Username, EmailAddress, PasswordHash, PasswordSalt, Created)
                    VALUES (@OrganisationId, 'Backup', 'User', @Username, 'backup@example.com', @Hash, @Salt, @Now);
                    """,
                    new { OrganisationId = organisationId, Username, Hash = hash, Salt = salt, Now = DateTime.UtcNow });

                await using (var destination = new SqliteConnection(
                    new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString()))
                {
                    await destination.OpenAsync();
                    source.BackupDatabase(destination);
                }

                // Written after the snapshot: it must not appear in the backup or after restore.
                await source.ExecuteAsync(
                    """
                    INSERT INTO User (OrganisationId, FirstName, LastName, Username, EmailAddress, PasswordHash, PasswordSalt, Created)
                    VALUES (@OrganisationId, 'Backup', 'Canary', 'backup-canary', 'canary@example.com', 'hash', 'salt', @Now);
                    """,
                    new { OrganisationId = organisationId, Now = DateTime.UtcNow });
            }

            await using (var backup = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false }.ToString()))
            {
                await backup.OpenAsync();
                Require(await backup.ExecuteScalarAsync<string>("PRAGMA integrity_check;") == "ok",
                    "The online backup must pass an integrity check.");
                Require(await backup.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM User WHERE Username = 'backup-canary';") == 0,
                    "The online backup must be a snapshot taken before later writes.");
            }

            // Restore as documented: with no process using it, replace the database file.
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                File.Delete(path + suffix);
            }
            File.Copy(backupPath, path);

            await using (var restored = new SqliteConnection(connectionString))
            {
                await restored.OpenAsync();
                Require(await restored.ExecuteScalarAsync<string>("PRAGMA integrity_check;") == "ok",
                    "The restored database must pass an integrity check.");
                Require(await restored.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM User WHERE Username = @Username;", new { Username }) == 1,
                    "The restored database must contain the account captured in the backup.");
                Require(await restored.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM User WHERE Username = 'backup-canary';") == 0,
                    "The restored database must not contain writes made after the backup.");
                Require(await restored.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM __Migrations;") == migrations,
                    "The restored database must keep its applied migration history.");
            }

            using var unitOfWork = new UnitOfWork(new SQliteConnectionFactory(connectionString));
            var login = await CreateService(unitOfWork).ValidateCredentialsAsync(Username, Password);
            Require(login.Success, "The restored account must authenticate through the application service.");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(path + suffix); } catch { /* best effort */ }
                try { File.Delete(backupPath + suffix); } catch { /* best effort */ }
            }
        }

        Console.WriteLine("PASS: a WAL-mode online backup restores cleanly, keeps migration history, drops later writes, and the restored account authenticates.");
    }

    private static int ApplyMigrations(string connectionString)
    {
        var migrations = typeof(Migration).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .Select(type => (Migration)Activator.CreateInstance(type)!)
            .OrderBy(migration => migration.MigrationId)
            .ToList();

        foreach (var migration in migrations)
        {
            migration.Apply(connectionString);
        }

        return migrations.Count;
    }

    private static AuthService CreateService(IUnitOfWork unitOfWork) =>
        new(
            unitOfWork,
            Options.Create(new JwtSettings
            {
                SecurityKey = SigningKey,
                Issuer = "backup-restore-checks",
                Audience = "backup-restore-checks",
                ExpirationMinutes = 5
            }),
            NullLogger<AuthService>.Instance,
            new LoginAttemptTracker(TimeProvider.System),
            new RefreshTokenStore(TimeProvider.System));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
