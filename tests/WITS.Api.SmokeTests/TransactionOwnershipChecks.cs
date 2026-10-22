using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using WITS.Data;
using WITS.Data.Entity;
using WITS.Data.Factory;

namespace WITS.Api.SmokeTests;

internal static class TransactionOwnershipChecks
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", $"wits-uow-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        try
        {
            using (var setup = new SqliteConnection(connectionString))
            {
                await setup.OpenAsync();
                await setup.ExecuteAsync("""
                    CREATE TABLE Organisation (
                        Id INTEGER PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Code TEXT NOT NULL,
                        Description TEXT NULL,
                        IsDeleted INTEGER NOT NULL DEFAULT 0,
                        Created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        LastModified TEXT NULL
                    );
                    """);
            }

            // (a) Multiple operations in one transaction, committed, and visible afterwards.
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>();
                uow.BeginTransaction(); // default level must be SQLite-supported
                var id = await repository.InsertAsync(new Organisation { Id = 0, Name = "Alpha", Code = "ALPHA" });
                await repository.UpdateAsync(new Organisation { Id = id, Name = "Alpha2", Code = "ALPHA" });
                var fetched = await repository.GetByIdAsync(id);
                Require(fetched is not null && fetched.Name == "Alpha2",
                    "a repository must read its own uncommitted write within the transaction");
                uow.Commit();
            }
            using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                var count = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Alpha2'");
                Require(count == 1, "committed rows must be visible outside the unit of work");
            }

            // (b) Rollback discards the transaction's writes.
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>();
                uow.BeginTransaction();
                await repository.InsertAsync(new Organisation { Id = 0, Name = "Beta", Code = "BETA" });
                uow.Rollback();
            }
            using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                var count = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Beta'");
                Require(count == 0, "rolled-back rows must not persist");
            }

            // (c) Repository reuse across transaction boundaries: a repository cached before
            // BeginTransaction must still enlist, and must not retain a disposed transaction.
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>(); // cached with no active transaction
                await repository.InsertAsync(new Organisation { Id = 0, Name = "Before", Code = "BEFORE" }); // autocommit

                uow.BeginTransaction();
                await repository.InsertAsync(new Organisation { Id = 0, Name = "Rolled", Code = "ROLLED" });
                uow.Rollback();

                uow.BeginTransaction();
                await repository.InsertAsync(new Organisation { Id = 0, Name = "Final", Code = "FINAL" });
                uow.Commit();
            }
            using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                var before = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Before'");
                var rolled = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Rolled'");
                var final = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Final'");
                Require(before == 1 && rolled == 0 && final == 1,
                    "repository reuse across transactions must enlist, rollback, and commit correctly");
            }

            Console.WriteLine("PASS: multiple operations, commit, rollback, and repository reuse work on a temporary database.");
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                try { File.Delete(path + suffix); } catch { /* best effort */ }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
