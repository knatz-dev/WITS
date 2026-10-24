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
        // Opt in only when rollback preserves application data and restores the prior schema.
        public virtual bool SupportsRollback => false;

        internal bool IsApplied(SqliteConnection connection, SqliteTransaction? transaction = null)
        {
            using var command = new SqliteCommand("SELECT Name, Script FROM __Migrations WHERE MigrationId = @MigrationId", connection, transaction);
            command.Parameters.AddWithValue("@MigrationId", MigrationId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return false;
            }
            if (reader.GetString(0) != Name || reader.GetString(1).ReplaceLineEndings("\n") != UpScript.ReplaceLineEndings("\n"))
            {
                throw new InvalidOperationException($"Applied migration {MigrationId} differs from this build. Applied migrations are immutable; use a matching build or a fresh database, and add future changes as new migrations.");
            }
            return true;
        }

        internal static void EnsureDatabaseDirectory(string connectionString)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (builder.Mode != SqliteOpenMode.ReadWriteCreate || builder.DataSource == ":memory:")
            {
                return;
            }
            var dataSource = builder.DataSource;
            var dataDirectory = Path.GetDirectoryName(dataSource);

            if (!string.IsNullOrEmpty(dataDirectory) && !Directory.Exists(dataDirectory))
            {
                Console.WriteLine($"Creating data directory at {dataDirectory}");
                Directory.CreateDirectory(dataDirectory);
            }
        }

        public void Apply(string connectionString)
        {
            EnsureDatabaseDirectory(connectionString);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                Migrator.EnsureMigrationsTable(connection, transaction);
                if (!IsApplied(connection, transaction))
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
            EnsureDatabaseDirectory(connectionString);

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var transaction = connection.BeginTransaction();
            try
            {
                Migrator.EnsureMigrationsTable(connection, transaction);
                if (!IsApplied(connection, transaction))
                {
                    transaction.Commit();
                    return;
                }

                using var latest = new SqliteCommand("SELECT MigrationId FROM __Migrations ORDER BY MigrationId DESC LIMIT 1", connection, transaction);
                if ((string?)latest.ExecuteScalar() != MigrationId)
                {
                    throw new InvalidOperationException($"Only the latest applied migration can be rolled back; {MigrationId} has later applied migrations.");
                }
                if (!SupportsRollback)
                {
                    throw new InvalidOperationException($"Migration {MigrationId} is forward-only: its rollback has not been verified to preserve data and restore the prior schema. Use a matching backup/build or a fresh database instead.");
                }

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
