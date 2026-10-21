using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using WITS.Data.Entity;
using WITS.Data.Factory;
using WITS.Data.Repository;

namespace WITS.Api.SmokeTests;

internal static class RepositoryFailureChecks
{
    public static async Task RunAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"repository-smoke-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var repository = new GenericRepository<Organisation, int>(new SQliteConnectionFactory(connectionString));

        await ExpectSqliteFailureAsync(() => repository.GetAllAsync(), 1, "Organisation");

        await keeper.ExecuteAsync("""
            CREATE TABLE Organisation (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Code TEXT NOT NULL,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                Created TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);
        var empty = await repository.GetAllAsync(pageNumber: 3, pageSize: 7);
        if (empty.Items.Count != 0 || empty.TotalRecords != 0 || empty.PageNumber != 3 || empty.PageSize != 7)
        {
            throw new InvalidOperationException("A genuine empty query must preserve its paging metadata.");
        }

        await keeper.ExecuteAsync("""
            INSERT INTO Organisation (Id, Name, Code, IsDeleted) VALUES
                (1, 'Visible', 'VISIBLE', 0), (2, 'Deleted', 'DELETED', 1);
            """);
        var populated = await repository.GetAllAsync(pageSize: 1);
        if (populated.Items.Count != 1 || populated.Items[0].Id != 1 || populated.TotalRecords != 1)
        {
            throw new InvalidOperationException("A successful query must return active records and their count.");
        }

        // COUNT succeeds, but materializing the rows fails after the second query.
        await keeper.ExecuteAsync("UPDATE Organisation SET Created = 'not-a-date' WHERE Id = 1");
        try
        {
            await repository.GetAllAsync();
            throw new InvalidOperationException("A row-mapping failure was reported as a successful result.");
        }
        catch (DataException exception) when (exception.Message.Contains("Created", StringComparison.Ordinal))
        {
            // The original Dapper diagnostic reached the caller.
        }

        var unavailable = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(Path.GetTempPath(), "opencode", $"missing-{Guid.NewGuid():N}.db"),
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        var unavailableRepository = new GenericRepository<Organisation, int>(new SQliteConnectionFactory(unavailable));
        await ExpectSqliteFailureAsync(() => unavailableRepository.GetAllAsync(), 14, "unable to open database file");

        Console.WriteLine("PASS: empty/populated pages stay successful; connection, SQL and row-mapping failures propagate.");
    }

    private static async Task ExpectSqliteFailureAsync(Func<Task> operation, int errorCode, string diagnostic)
    {
        try
        {
            await operation();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == errorCode
            && exception.Message.Contains(diagnostic, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"Expected SQLite error {errorCode} to reach the caller.");
    }
}
