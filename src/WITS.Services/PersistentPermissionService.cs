using Dapper;
using WITS.Data.Contracts;

namespace WITS.Services;

public sealed class PersistentPermissionService : IPermissionService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public PersistentPermissionService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public bool HasPermission(long userId, PermissionScope scope, long scopeId, string permission)
    {
        using var connection = _connectionFactory.CreateConnection();
        var count = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM UserGrants WHERE UserId = @UserId AND Scope = @Scope AND ScopeId = @ScopeId AND Permission = @Permission;",
            new { UserId = userId, Scope = scope.ToString(), ScopeId = scopeId, Permission = permission });
        return count > 0;
    }

    public void Grant(long userId, PermissionScope scope, long scopeId, string permission)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute(
            "INSERT OR IGNORE INTO UserGrants (UserId, Scope, ScopeId, Permission, Created) VALUES (@UserId, @Scope, @ScopeId, @Permission, @Created);",
            new { UserId = userId, Scope = scope.ToString(), ScopeId = scopeId, Permission = permission, Created = DateTime.UtcNow });
    }

    public void Revoke(long userId, PermissionScope scope, long scopeId, string permission)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute(
            "DELETE FROM UserGrants WHERE UserId = @UserId AND Scope = @Scope AND ScopeId = @ScopeId AND Permission = @Permission;",
            new { UserId = userId, Scope = scope.ToString(), ScopeId = scopeId, Permission = permission });
    }

    public void RevokeAll(long userId)
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Execute("DELETE FROM UserGrants WHERE UserId = @UserId;", new { UserId = userId });
    }
}