// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;

namespace WITS.Data.Factory;

public class SQliteDatabaseFactory
{
    private readonly SQliteConnectionFactory _connectionFactory;

    public SQliteDatabaseFactory(SQliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        CreateDatabaseFile();
    }

    private void CreateDatabaseFile()
    {
        string databasePath = GetDatabasePath()
            ?? throw new InvalidOperationException("Database file path is missing.");

        string directory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Directory not found!");

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(databasePath))
        {
            using var file = File.Create(databasePath);
        }
    }

    public async Task InitializeSchema(string initialSchema)
    {
        using IDbConnection connection = await _connectionFactory.CreateConnectionAsync();
        await connection.ExecuteAsync(initialSchema);
    }

    private string GetDatabasePath()
    {
        SqliteConnectionStringBuilder builder = new(_connectionFactory.CreateConnection().ConnectionString);
        return builder.DataSource;
    }
}
