namespace WITS.Services;

public enum PermissionScope { Organisation, Project }

public interface IPermissionService
{
    bool HasPermission(long userId, PermissionScope scope, long scopeId, string permission);
    void Grant(long userId, PermissionScope scope, long scopeId, string permission);
    void Revoke(long userId, PermissionScope scope, long scopeId, string permission);
    void RevokeAll(long userId);
}

// shortcut: grants are process-local and reset on restart; load from UserRole/ProjectUserRole/RolePermissions before multi-instance use.
public sealed class InMemoryPermissionService : IPermissionService
{
    private readonly object _gate = new();
    private readonly Dictionary<(long UserId, PermissionScope Scope, long ScopeId), HashSet<string>> _grants = new();

    public bool HasPermission(long userId, PermissionScope scope, long scopeId, string permission)
    {
        lock (_gate)
        {
            return _grants.TryGetValue((userId, scope, scopeId), out var set) && set.Contains(permission);
        }
    }

    public void Grant(long userId, PermissionScope scope, long scopeId, string permission)
    {
        lock (_gate)
        {
            var key = (userId, scope, scopeId);
            if (!_grants.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _grants[key] = set;
            }
            set.Add(permission);
        }
    }

    public void Revoke(long userId, PermissionScope scope, long scopeId, string permission)
    {
        lock (_gate)
        {
            if (_grants.TryGetValue((userId, scope, scopeId), out var set)) set.Remove(permission);
        }
    }

    public void RevokeAll(long userId)
    {
        lock (_gate)
        {
            foreach (var key in _grants.Keys.Where(k => k.UserId == userId).ToArray()) _grants.Remove(key);
        }
    }
}
