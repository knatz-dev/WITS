using System.Reflection;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using WITS.Migrator.Migrations;
// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

namespace WITS.Migrator
{
    public class Migrator
    {
        private readonly string _connectionString;

        public Migrator(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Creates a new migration file.
        /// </summary>
        /// <param name="parameters">Command parameters containing migration name</param>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public static int Create(string[] parameters)
        {
            if (parameters.Length < 1)
            {
                AnsiConsole.MarkupLine("[red]Please specify migration name[/]");
                return 1;
            }

            var name = parameters[0];
            // Generate timestamp in format YYYYMMDDHHMMSS
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var migrationId = timestamp;
            var className = $"Migration{timestamp}{name}";
            var fileName = $"{timestamp}_{name}.cs";
            var migrationsPath = Path.Combine(Directory.GetCurrentDirectory(), "Migrations");

            // Ensure Migrations directory exists
            if (!Directory.Exists(migrationsPath))
            {
                Directory.CreateDirectory(migrationsPath);
            }

            var filePath = Path.Combine(migrationsPath, fileName);

            // Check if file already exists
            if (File.Exists(filePath))
            {
                AnsiConsole.MarkupLine($"[red]Migration file {fileName} already exists[/]");
                return 0;
            }

            // Create migration file content
            var content = $@"
                using Microsoft.Data.Sqlite;

                namespace WITS.Migrator.Migrations
                {{
                    public class {className} : Migration
                    {{
                        public override string MigrationId => ""{migrationId}"";
                        public override string Name => ""{name}"";

                        public override string UpScript => @""
                            -- TODO: Add your migration SQL here
                            -- Example: CREATE TABLE IF NOT EXISTS TableName (
                            --     Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            --     CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                            -- )"";

                         public override string DownScript => @""
                            -- Rollback is disabled by default. Override SupportsRollback only after verifying
                            -- that this script preserves application data and restores the prior schema.
                             -- TODO: Add your rollback SQL here
                            -- Example: DROP TABLE IF EXISTS TableName"";
                    }}
                }}
            ";

            // Write the file
            File.WriteAllText(filePath, content);
            AnsiConsole.MarkupLine($"[green]Created migration file: {fileName}[/]");
            AnsiConsole.MarkupLine("[yellow]Please edit the UpScript and DownScript with your migration SQL[/]");

            return 0;
        }

        /// <summary>
        /// Creates missing history and rejects incompatible history without repairing it.
        /// </summary>
        /// <param name="connection">Database connection</param>
        public static void EnsureMigrationsTable(SqliteConnection connection, SqliteTransaction? transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name = '__Migrations' COLLATE NOCASE";
            if ((long)command.ExecuteScalar()! == 0)
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE name NOT GLOB 'sqlite_*'";
                if ((long)command.ExecuteScalar()! != 0)
                {
                    throw new InvalidOperationException("Untracked application schema: __Migrations is missing. This build supports fresh databases only; use a new empty database or a matching backup/build instead of guessing migration history.");
                }
                command.CommandText = """
                CREATE TABLE __Migrations (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    MigrationId TEXT NOT NULL UNIQUE,
                    Name TEXT NOT NULL,
                    AppliedAt DATETIME NOT NULL,
                    Script TEXT NOT NULL
                );
                """;
                command.ExecuteNonQuery();
            }

            // Validate before any write to existing history, including attempts to add indexes.
            command.CommandText = """
                SELECT
                    EXISTS (SELECT 1 FROM sqlite_schema
                            WHERE name = '__Migrations' COLLATE NOCASE AND type = 'table')
                    AND (SELECT COUNT(*) FROM pragma_table_xinfo('__Migrations')) = 5
                    AND (SELECT COUNT(*) FROM pragma_table_xinfo('__Migrations')
                         WHERE hidden = 0 AND (
                             (name = 'Id' AND upper(type) = 'INTEGER' AND pk = 1)
                             OR (name IN ('MigrationId', 'Name', 'Script')
                                 AND upper(type) = 'TEXT' AND "notnull" = 1 AND pk = 0)
                             OR (name = 'AppliedAt' AND upper(type) = 'DATETIME'
                                 AND "notnull" = 1 AND pk = 0))) = 5
                    AND EXISTS (
                        SELECT 1 FROM pragma_index_list('__Migrations') AS indexes
                        WHERE indexes."unique" = 1 AND indexes.partial = 0
                          AND (SELECT COUNT(*) FROM pragma_index_info(indexes.name)) = 1
                          AND EXISTS (SELECT 1 FROM pragma_index_info(indexes.name)
                                      WHERE name = 'MigrationId'));
                """;
            if ((long)command.ExecuteScalar()! != 1)
            {
                throw new InvalidOperationException("Incompatible __Migrations schema. Restore a valid backup or explicitly repair the history schema before retrying; history was not replaced.");
            }

            command.CommandText = """
                SELECT EXISTS (SELECT 1 FROM __Migrations
                    WHERE typeof(MigrationId) <> 'text' OR length(trim(MigrationId)) = 0
                       OR typeof(Name) <> 'text' OR length(trim(Name)) = 0
                       OR typeof(AppliedAt) <> 'text' OR julianday(AppliedAt) IS NULL
                       OR typeof(Script) <> 'text');
                """;
            if ((long)command.ExecuteScalar()! == 1)
            {
                throw new InvalidOperationException("Invalid __Migrations records. Restore a valid backup or explicitly repair the history records before retrying; history was not replaced.");
            }

            command.CommandText = """
                SELECT NOT EXISTS (SELECT 1 FROM __Migrations)
                    AND EXISTS (SELECT 1 FROM sqlite_schema
                                WHERE name NOT GLOB 'sqlite_*' AND tbl_name <> '__Migrations' COLLATE NOCASE);
                """;
            if ((long)command.ExecuteScalar()! == 1)
            {
                throw new InvalidOperationException("Untracked application schema: __Migrations is empty but application objects exist. Use a fresh database or a matching backup/build instead of inventing applied history.");
            }
        }

        /// <summary>
        /// Gets a list of applied migrations from the database.
        /// </summary>
        /// <returns></returns>
        public HashSet<string> GetAppliedMigrations()
        {
            var appliedMigrations = new HashSet<string>();
            Migration.EnsureDatabaseDirectory(_connectionString);
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            EnsureMigrationsTable(connection);
            using var cmd = new SqliteCommand("SELECT MigrationId FROM __Migrations", connection);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                appliedMigrations.Add(reader.GetString(0));
            }

            return appliedMigrations;
        }


        /// <summary>
        /// Gets a list of all migration files in the Migrations directory.
        /// </summary>
        /// <returns>List of migration file paths</returns>
        public static List<string> GetMigrationFiles()
        {
            var migrationsPath = Path.Combine(Directory.GetCurrentDirectory(), "Migrations");
            return Directory.GetFiles(migrationsPath, "*.cs").ToList();
        }

        /// <summary>
        ///  Retrieves all non-abstract migrations defined in the current assembly, ordered by their type name.
        /// </summary>
        /// <remarks>This method scans the executing assembly for types that inherit from the <see
        /// cref="Migration"/> class and are not abstract. It then creates instances of these types and returns them as
        /// an enumerable collection.</remarks>
        /// <returns>An <see cref="IEnumerable{T}"/> containing instances of all non-abstract migrations in the current assembly,
        /// ordered by their type name. If no migrations are found, the collection will be empty.</returns>
        public static IEnumerable<Migration> GetMigrations()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            IEnumerable<Migration> returnVal = assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.IsAbstract)
                .Select(t => Activator.CreateInstance(t) as Migration) // Use safe cast to handle nulls
                .Where(instance => instance is not null) // Filter out null instances
                .Cast<Migration>()
                .OrderBy(m => m.MigrationId);
            return returnVal;
        }

        /// <summary>
        /// Gets the status of migrations in the database.
        /// </summary>
        /// <param name="connection">Database connection</param>
        /// <returns>Dictionary of migration IDs and their status</returns>
        public static async Task<Dictionary<string, string>> GetMigrationStatus(SqliteConnection connection)
        {
            EnsureMigrationsTable(connection);
            var appliedMigrations = new Dictionary<string, string>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __Migrations;";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                appliedMigrations.Add(reader.GetString(0), "Applied");
            }
            return appliedMigrations;
        }

        /// <summary>
        /// Gets a list of all tables in the database.
        /// </summary>
        /// <param name="connection">Database connection</param>
        /// <returns>List of table names</returns>
        public static async Task<List<string>> GetTables(SqliteConnection connection)
        {
            var tables = new List<string>();
            var command = connection.CreateCommand();
            command.CommandText = """
                SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'Migrations';
            """;
                
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
            return tables;
        }

        /// <summary>
        /// Lists all migrations and database tables.
        /// </summary>
        /// <param name="connectionString">Database connection string</param>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int List()
        {
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'Migrations';";
                var reader = command.ExecuteReader();

                var table = new Table();
                table.Border(TableBorder.Rounded);
                table.AddColumn(new TableColumn("[blue]Table Name[/]").Centered());

                var hasRows = false;
                while (reader.Read())
                {
                    hasRows = true;
                    table.AddRow(reader.GetString(0));
                }

                if (!hasRows)
                {
                    AnsiConsole.MarkupLine("[yellow]No tables found in the database.[/]");
                    return 0;
                }

                AnsiConsole.Write(table);
            }

            return 0;
        }

        /// <summary>
        /// Lists all migrations in the database.
        /// </summary>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int ListMigrations()
        {
            HashSet<string> appliedMigrations = GetAppliedMigrations();
            IEnumerable<Migration> migrations = GetMigrations();

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("[blue]Migration ID[/]").Centered());
            table.AddColumn(new TableColumn("[blue]Name[/]").Centered());
            table.AddColumn(new TableColumn("[blue]Status[/]").Centered());

            foreach (Migration migration in migrations)
            {
                string status = appliedMigrations.Contains(migration.MigrationId) ? "[green]Applied[/]" : "[yellow]Pending[/]";
                table.AddRow(migration.MigrationId, migration.Name, status);
            }

            AnsiConsole.Write(table);
            return 0;
        }

        /// <summary>
        /// Lists all tables in the database.
        /// </summary>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int ListTables() => List();

        /// <summary>
        /// Applies all pending migrations to the database.
        /// </summary>
        /// <param name="connectionString">Database connection string</param>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int Migrate()
        {
            var migrations = GetMigrations().ToList();
            if (!migrations.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No migrations found to apply.[/]");
                return 0;
            }

            var applied = GetAppliedMigrations();
            if (!applied.SetEquals(migrations.Take(applied.Count).Select(m => m.MigrationId)))
            {
                throw new InvalidOperationException("Unsupported migration history: applied IDs must be a contiguous prefix of this build's migrations. Unknown or missing migrations require a matching build or a fresh database.");
            }
            using (var connection = new SqliteConnection(_connectionString))
            {
                connection.Open();
                foreach (var migration in migrations.Take(applied.Count))
                {
                    if (!migration.IsApplied(connection))
                    {
                        throw new InvalidOperationException("Migration history changed during validation. Stop other migration processes and retry.");
                    }
                }
            }
            var pending = migrations.Where(m => !applied.Contains(m.MigrationId)).ToList();
            if (!pending.Any())
            {
                AnsiConsole.MarkupLine("[green]All migrations are up to date.[/]");
                return 0;
            }

            var table = new Table();
            table.Border(TableBorder.Rounded);
            table.AddColumn(new TableColumn("[blue]Migration ID[/]").Centered());
            table.AddColumn(new TableColumn("[blue]Name[/]").Centered());
            table.AddColumn(new TableColumn("[blue]Status[/]").Centered());

            foreach (var migration in pending)
            {
                try
                {
                    AnsiConsole.MarkupLine($"Applying migration: [blue]{migration.Name}[/]");
                    migration.Apply(_connectionString);
                    table.AddRow(migration.MigrationId, migration.Name, "[green]Applied[/]");
                }
                catch (Exception ex)
                {
                    table.AddRow(migration.MigrationId, migration.Name, "[red]Failed[/]");
                    AnsiConsole.MarkupLine($"[red]Error applying migration {Markup.Escape(migration.Name)}: {Markup.Escape(ex.Message)}[/]");
                    throw;
                }
            }

            AnsiConsole.Write(table);
            return 0;
        }

        /// <summary>
        /// Removes a specific migration or all migrations.
        /// </summary>
        /// <param name="parameters">Command parameters containing migration ID or 'all'</param>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int Remove(string[] parameters)
        {
            var migrations = GetMigrations().ToList();
            var appliedMigrations = GetAppliedMigrations();

            if (parameters.Length < 1)
            {
                AnsiConsole.MarkupLine("[red]Please specify migration ID to remove, or use 'all' to remove all pending migrations[/]");
                return 1;
            }

            var migrationId = parameters[0];

            if (migrationId.ToLower() == "all")
            {
                var pendingMigrations = migrations.Where(m => !appliedMigrations.Contains(m.MigrationId)).ToList();
                if (!pendingMigrations.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]No pending migrations to remove.[/]");
                    return 0;
                }

                var table = new Table();
                table.Border(TableBorder.Rounded);
                table.AddColumn(new TableColumn("[blue]Migration ID[/]").Centered());
                table.AddColumn(new TableColumn("[blue]Name[/]").Centered());
                table.AddColumn(new TableColumn("[blue]Status[/]").Centered());

                var failedRemovals = new List<string>();
                foreach (var migration in pendingMigrations)
                {
                    var removed = RemoveMigrationFile(migration.MigrationId);
                    table.AddRow(migration.MigrationId, migration.Name, removed ? "[green]Removed[/]" : "[red]Failed[/]");
                    if (!removed)
                    {
                        failedRemovals.Add(migration.MigrationId);
                    }
                }

                AnsiConsole.Write(table);

                if (failedRemovals.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[red]Could not remove {failedRemovals.Count} migration file(s): {string.Join(", ", failedRemovals)}[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[green]Removed {pendingMigrations.Count} pending migration(s).[/]");
            }
            else
            {
                var migration = migrations.FirstOrDefault(m => m.MigrationId == migrationId);
                if (migration == null)
                {
                    AnsiConsole.MarkupLine($"[red]Migration {migrationId} not found[/]");
                    return 1;
                }

                if (appliedMigrations.Contains(migrationId))
                {
                    AnsiConsole.MarkupLine($"[red]Cannot remove migration {migrationId} because it has been applied. Rollback first.[/]");
                    return 1;
                }


                if (!RemoveMigrationFile(migrationId))
                {
                    AnsiConsole.MarkupLine($"[red]Could not remove migration {migrationId}[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine($"[green]Removed migration {migrationId}[/]");
            }

            return 0;
        }

        public static bool RemoveMigrationFile(string migrationId)
        {
            var migrationsPath = Path.Combine(Directory.GetCurrentDirectory(), "Migrations");
            var files = Directory.GetFiles(migrationsPath, $"{migrationId}_*.cs");

            var removedAll = true;
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                    AnsiConsole.MarkupLine($"Deleted file: [blue]{Path.GetFileName(file)}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error deleting file {file}: {ex.Message}[/]");
                    removedAll = false;
                }
            }

            return removedAll;
        }

        /// <summary>
        /// Rolls back a specific migration.
        /// </summary>
        /// <param name="parameters">Command parameters containing migration ID</param>
        /// <returns>Exit code (0 for success, non-zero for failure)</returns>
        public int Rollback(string[] parameters)
        {
            if (parameters.Length < 1)
            {
                AnsiConsole.MarkupLine("[red]Please specify migration ID to rollback[/]");
                return 1;
            }

            var migrationId = parameters[0];
            var migrations = GetMigrations();
            var migration = migrations.FirstOrDefault(m => m.MigrationId == migrationId);

            if (migration == null)
            {
                AnsiConsole.MarkupLine($"[red]Migration {migrationId} not found[/]");
                return 1;
            }

            try
            {
                AnsiConsole.MarkupLine($"Rolling back migration: [blue]{migration.Name}[/]");
                migration.Rollback(_connectionString);
                AnsiConsole.MarkupLine("[green]Migration rolled back successfully[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error rolling back migration {migration.Name}: {ex.Message}[/]");
                throw;
            }

            return 0;
        }



        /// <summary>
        /// Displays help information for available commands.
        /// </summary>
        /// <returns>Always returns 1 to indicate help was shown</returns>
        public static int ShowHelp()
        {
            var table = new Table();
            table.AddColumn("Command");
            table.AddColumn("Description");
            table.AddColumn("Usage");

            table.AddRow("migrate", "Apply all pending migrations", "migrate");
            table.AddRow("rollback", "Rollback a specific migration", "rollback <migration_id>");
            table.AddRow("list", "List all migrations and tables", "list");
            table.AddRow("list-migrations", "List all migrations", "list-migrations");
            table.AddRow("list-tables", "List all database tables", "list-tables");
            table.AddRow("remove", "Remove a migration", "remove <migration_id|all>");
            table.AddRow("create", "Create a new migration", "create <migration_name>");

            AnsiConsole.Write(table);
            return 1;
        }
    }
}
