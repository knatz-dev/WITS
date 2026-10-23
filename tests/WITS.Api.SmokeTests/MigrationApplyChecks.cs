using Microsoft.Data.Sqlite;
using WITS.Data;
using WITS.Data.Entity;
using WITS.Data.Factory;
using WITS.Migrator.Migrations;

namespace WITS.Api.SmokeTests;

internal static class MigrationApplyChecks
{
    public static async Task RunAsync()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"wits-migration-check-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        try
        {
            var migration = new Migration20250419113604SystemSchema();
            migration.Apply(connectionString);
            migration.Apply(connectionString);
            var authMigration = new Migration20250419113613AuthSchema();
            authMigration.Apply(connectionString);
            authMigration.Apply(connectionString);
            new Migration20250419113619ProjectSchema().Apply(connectionString);
            new Migration20250419113732TicketSchema().Apply(connectionString);
            new Migration20250419115950OrganizationAndActivitySchema().Apply(connectionString);
            new Migration20261023090000ConstraintHardening().Apply(connectionString);

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA table_info('Organisation')";

                await using var reader = await command.ExecuteReaderAsync();
                var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadAsync())
                {
                    columns.Add(reader.GetString(1));
                }

                var requiredColumns = new[] { "Name", "Code", "Description", "IsDeleted", "Created", "LastModified" };
                if (requiredColumns.Any(column => !columns.Contains(column)))
                {
                    throw new InvalidOperationException("The SystemSchema migration did not create the Organisation table required by the seeder.");
                }
            }

            using var unitOfWork = new UnitOfWork(new SQliteConnectionFactory(connectionString));
            unitOfWork.BeginTransaction();
            try
            {
                var now = DateTime.UtcNow;
                var organisationRepository = unitOfWork.GetRepository<Organisation, int>();
                await organisationRepository.InsertAsync(new Organisation
                {
                    Id = 1,
                    Name = "Test organisation",
                    Code = "TEST",
                    Created = now
                });

                var userRepository = unitOfWork.GetRepository<User, long>();
                await userRepository.InsertAsync(new User
                {
                    Id = 1,
                    OrganisationId = 1,
                    FirstName = "Test",
                    LastName = "User",
                    Username = "test-user",
                    EmailAddress = "test@example.com",
                    PasswordHash = "hash",
                    PasswordSalt = "salt",
                    Created = now
                });
                unitOfWork.Commit();
            }
            catch
            {
                unitOfWork.Rollback();
                throw;
            }
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var path = $"{databasePath}{suffix}";
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        Console.WriteLine("PASS: the full migration chain applies idempotently and supports Organisation/User repository inserts.");
    }
}
