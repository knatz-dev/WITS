using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class PermissionChecks
{
    public static Task RunAsync()
    {
        var service = new InMemoryPermissionService();
        service.Grant(1, PermissionScope.Organisation, 10, "tickets.read");
        service.Grant(1, PermissionScope.Project, 5, "tickets.write");
        Require(service.HasPermission(1, PermissionScope.Organisation, 10, "tickets.read"), "Granted org permission must allow.");
        Require(!service.HasPermission(1, PermissionScope.Organisation, 10, "tickets.write"), "Ungranted org permission must deny.");
        Require(!service.HasPermission(1, PermissionScope.Organisation, 11, "tickets.read"), "Cross-scope (different org) must deny.");
        Require(service.HasPermission(1, PermissionScope.Project, 5, "tickets.write"), "Granted project permission must allow.");
        Require(!service.HasPermission(1, PermissionScope.Project, 6, "tickets.write"), "Cross-project must deny.");
        Require(!service.HasPermission(2, PermissionScope.Organisation, 10, "tickets.read"), "Different user must deny.");
        service.Revoke(1, PermissionScope.Organisation, 10, "tickets.read");
        Require(!service.HasPermission(1, PermissionScope.Organisation, 10, "tickets.read"), "Revoked permission must deny.");
        service.Grant(3, PermissionScope.Organisation, 1, "a");
        service.Grant(3, PermissionScope.Project, 2, "b");
        service.RevokeAll(3);
        Require(!service.HasPermission(3, PermissionScope.Organisation, 1, "a") && !service.HasPermission(3, PermissionScope.Project, 2, "b"),
            "RevokeAll must clear every grant for the user.");
        Console.WriteLine("PASS: scoped role/permission evaluation allows grants, denies cross-scope/cross-user, and revokes.");
        return Task.CompletedTask;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}