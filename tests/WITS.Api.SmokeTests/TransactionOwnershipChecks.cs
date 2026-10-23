using System.Data;
using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;
using WITS.Data;
using WITS.Data.Entity;
using WITS.Data.Factory;
using WITS.Data.Repository;

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
                        LastModified TEXT NULL,
                        RowVersion INTEGER NOT NULL DEFAULT 1
                    );
                    """);
            }

            // (a) Multiple operations in one transaction, committed, and visible afterwards.
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>();
                uow.BeginTransaction(); // default level must be SQLite-supported
                var alpha = new Organisation { Id = 0, Name = "Alpha", Code = "ALPHA" };
                var id = await repository.InsertAsync(alpha);
                alpha.Id = id;
                alpha.Name = "Alpha2";
                Require(await repository.UpdateAsync(alpha), "updating a freshly inserted row must succeed");
                var fetched = await repository.GetByIdAsync(id);
                Require(fetched is not null && fetched.Name == "Alpha2" && fetched.RowVersion == 2,
                    "a repository must read its own uncommitted write within the transaction");
                uow.Commit();
            }
            using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                var count = await verify.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Organisation WHERE Name = 'Alpha2'");
                Require(count == 1, "committed rows must be visible outside the unit of work");
            }

            // (a2) Optimistic concurrency: a stale writer cannot overwrite the winner.
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>();
                var gamma = new Organisation { Id = 0, Name = "Gamma", Code = "GAMMA" };
                var gammaId = await repository.InsertAsync(gamma);
                var first = await repository.GetByIdAsync(gammaId);
                var stale = await repository.GetByIdAsync(gammaId);
                Require(first is not null && stale is not null, "both writers must read the row");

                first!.Name = "Gamma-first";
                Require(await repository.UpdateAsync(first), "the first writer must succeed");

                stale!.Name = "Gamma-stale";
                Require(!await repository.UpdateAsync(stale), "a stale writer must be rejected");

                var current = await repository.GetByIdAsync(gammaId);
                Require(current is not null && current.Name == "Gamma-first" && current.RowVersion == 2,
                    "the stale write must not overwrite the committed value");
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

            // (d) Server-owned timestamps and soft deletion across every access path.
            var created = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
            int deltaId;
            using (var uow = new UnitOfWork(new SQliteConnectionFactory(connectionString)))
            {
                var repository = uow.GetRepository<Organisation, int>();
                var delta = new Organisation
                {
                    Id = 0,
                    Name = "Delta",
                    Code = "DELTA",
                    Created = created,
                    LastModified = DateTime.UtcNow,
                    IsDeleted = true
                };
                deltaId = await repository.InsertAsync(delta);
                delta.Id = deltaId;

                var fetched = await repository.GetByIdAsync(deltaId);
                Require(fetched is not null, "the inserted row must be readable");
                Require(!fetched!.IsDeleted, "insert must clear a caller-supplied soft-delete flag");
                Require(fetched.LastModified is null, "insert must not store a caller-supplied LastModified");
                Require(fetched.Created == created, "an explicit UTC Created value must be kept");

                // Updates may not backdate creation or bypass DeleteByIdAsync.
                fetched.Created = created.AddDays(30);
                fetched.IsDeleted = true;
                fetched.Name = "Delta2";
                Require(await repository.UpdateAsync(fetched), "a live row must update");
                var afterUpdate = await repository.GetByIdAsync(deltaId);
                Require(afterUpdate is not null, "an update must not soft-delete the row");
                Require(afterUpdate!.Created == created, "an update must not change Created");

                Require(await repository.DeleteByIdAsync(deltaId), "deleting a live row must report success");
                Require(await repository.GetByIdAsync(deltaId) is null, "by-ID reads must hide deleted rows");
                var page = await repository.GetAllAsync();
                Require(page.Items.All(o => o.Id != deltaId), "list reads must hide deleted rows");
                Require(!await repository.DeleteByIdAsync(deltaId), "deleting an already deleted row is a no-op");
            }
            using (var verify = new SqliteConnection(connectionString))
            {
                await verify.OpenAsync();
                var retained = await verify.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM Organisation WHERE Id = @Id AND IsDeleted = 1 AND LastModified IS NOT NULL",
                    new { Id = deltaId });
                Require(retained == 1, "soft deletion must retain the row and record LastModified");
            }

            Console.WriteLine("PASS: multiple operations, commit, rollback, optimistic concurrency, soft deletion, server timestamps, and repository reuse work on a temporary database.");
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
