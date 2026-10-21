// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using WITS.Data.Contracts;

namespace WITS.Data.Factory;

public class SQliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SQliteConnectionFactory(string connectionString)
    {
        _connectionString = connectionString
            ?? throw new ArgumentNullException(nameof(connectionString), "ConnectionString is missing");
    }

    public IDbConnection CreateConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA foreign_keys = ON;");
        return connection;
    }

    public async Task<IDbConnection> CreateConnectionAsync(CancellationToken token = default)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(token);
        await connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
        return connection;
    }
}
