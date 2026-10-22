// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public abstract class Migration
    {
        public abstract string MigrationId { get; }
        public abstract string Name { get; }
        public abstract string UpScript { get; }
        public abstract string DownScript { get; }

        protected static void EnsureDatabaseExists(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;
            var dataDirectory = Path.GetDirectoryName(dataSource);

            if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
            {
                Console.WriteLine($"Creating data directory at {dataDirectory}");
                Directory.CreateDirectory(dataDirectory);
            }

            if (!File.Exists(dataSource))
            {
                Console.WriteLine($"Database file not found at {dataSource}. Creating new database...");
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                // Create migrations table if it doesn't exist
                using var cmd = new SqliteCommand(@"
                    CREATE TABLE IF NOT EXISTS __Migrations (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        MigrationId TEXT NOT NULL UNIQUE,
                        Name TEXT NOT NULL,
                        AppliedAt DATETIME NOT NULL,
                        Script TEXT NOT NULL
                    );

                    CREATE INDEX IF NOT EXISTS idx_migrations_id ON __Migrations(MigrationId);", connection);
                cmd.ExecuteNonQuery();

                Console.WriteLine("Database and migrations table created successfully.");
            }
        }

        public void Apply(string connectionString)
        {
            EnsureDatabaseExists(connectionString);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Check if migration already applied
                var checkSql = "SELECT COUNT(*) FROM __Migrations WHERE MigrationId = @MigrationId";
                using var checkCmd = new SqliteCommand(checkSql, connection, transaction);
                checkCmd.Parameters.AddWithValue("@MigrationId", MigrationId);
                int exists = (int?)checkCmd.ExecuteScalar() ?? 0;

                if (exists == 0)
                {
                    // Apply migration
                    using var cmd = new SqliteCommand(UpScript, connection, transaction);
                    cmd.ExecuteNonQuery();

                    // Record migration
                    var insertSql = @"
                        INSERT INTO __Migrations (MigrationId, Name, AppliedAt, Script)
                        VALUES (@MigrationId, @Name, @AppliedAt, @Script)";
                    
                    using var insertCmd = new SqliteCommand(insertSql, connection, transaction);
                    insertCmd.Parameters.AddWithValue("@MigrationId", MigrationId);
                    insertCmd.Parameters.AddWithValue("@Name", Name);
                    insertCmd.Parameters.AddWithValue("@AppliedAt", DateTime.UtcNow);
                    insertCmd.Parameters.AddWithValue("@Script", UpScript);
                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackException)
                {
                    Console.Error.WriteLine($"Rollback after failed migration apply also failed: {rollbackException.Message}");
                }

                // Rethrow the original failure, not a rollback failure.
                throw;
            }
        }

        public void Rollback(string connectionString)
        {
            EnsureDatabaseExists(connectionString);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                // Apply rollback script
                using var cmd = new SqliteCommand(DownScript, connection, transaction);
                cmd.ExecuteNonQuery();

                // Remove migration record
                var deleteSql = "DELETE FROM __Migrations WHERE MigrationId = @MigrationId";
                using var deleteCmd = new SqliteCommand(deleteSql, connection, transaction);
                deleteCmd.Parameters.AddWithValue("@MigrationId", MigrationId);
                deleteCmd.ExecuteNonQuery();

                transaction.Commit();
            }
            catch
            {
                try
                {
                    transaction.Rollback();
                }
                catch (Exception rollbackException)
                {
                    Console.Error.WriteLine($"Rollback after failed migration rollback also failed: {rollbackException.Message}");
                }

                // Rethrow the original failure, not a rollback failure.
                throw;
            }
        }
    }
} 
