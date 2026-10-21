// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Data;
using WITS.Data.Contracts;

namespace WITS.Data.Factory;

internal class StaticConnectionFactory : IDbConnectionFactory
{
    private readonly IDbConnection _existingConnection;

    public StaticConnectionFactory(IDbConnection connection)
    {
        _existingConnection = connection;
    }

    public Task<IDbConnection> CreateConnectionAsync(CancellationToken token = default) => Task.FromResult(_existingConnection);

    IDbConnection IDbConnectionFactory.CreateConnection() => _existingConnection;

}
