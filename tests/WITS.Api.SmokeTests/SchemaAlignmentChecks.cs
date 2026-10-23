using Microsoft.Data.Sqlite;
using WITS.Common;
using WITS.Data;
using WITS.Data.Entity;
using WITS.Data.Factory;
using WITS.Migrator.Migrations;

namespace WITS.Api.SmokeTests;

internal static class SchemaAlignmentChecks
{
    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", $"wits-schema-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false
        }.ToString();

        try
        {
            // Apply the real migration chain so the checks run against the produced schema.
            new Migration20250419113604SystemSchema().Apply(connectionString);
            new Migration20250419113613AuthSchema().Apply(connectionString);
            new Migration20250419113619ProjectSchema().Apply(connectionString);
            new Migration20250419113732TicketSchema().Apply(connectionString);
            new Migration20250419115950OrganizationAndActivitySchema().Apply(connectionString);

            using var unitOfWork = new UnitOfWork(new SQliteConnectionFactory(connectionString));
            unitOfWork.BeginTransaction();
            var now = DateTime.UtcNow;

            var organisations = unitOfWork.GetRepository<Organisation, int>();
            var organisationId = await organisations.InsertAsync(new Organisation
            {
                Id = 0, Name = "Smoke organisation", Code = "SMOKE", Created = now
            });
            Require(organisationId > 0, "Organisation insert must return a generated key.");

            // User carries [NotMapped] navigation and a computed property; the insert succeeding
            // proves neither is written as a column.
            var users = unitOfWork.GetRepository<User, long>();
            var userId = await users.InsertAsync(new User
            {
                Id = 0,
                OrganisationId = organisationId,
                FirstName = "Smoke",
                LastName = "User",
                Username = "smoke-user",
                EmailAddress = "smoke@example.com",
                PasswordHash = "hash",
                PasswordSalt = "salt",
                Created = now
            });
            var user = await users.GetByIdAsync(userId);
            Require(user is { FullName: "Smoke User" }, "User must round-trip with its computed FullName.");

            var roles = unitOfWork.GetRepository<Role, short>();
            var roleId = (short)await roles.InsertAsync(new Role
            {
                Id = 0, Name = "Smoke role", Code = "SMOKE", Created = now
            });
            var role = await roles.GetByIdAsync(roleId);
            Require(role is { Code: "SMOKE" }, "Role must map through [Table] to the Roles table.");
            role!.Name = "Smoke role renamed";
            Require(await roles.UpdateAsync(role), "Role update must affect the migrated table.");

            var roleQuery = await roles.QueryAsync(new GenericFilter
            {
                Filters = new Dictionary<string, FilterCriteria> { ["Name"] = new FilterCriteria("Smoke role renamed") }
            });
            Require(roleQuery.Items.Count == 1 && roleQuery.Items[0].Code == "SMOKE",
                "Filtered queries must use persisted columns.");

            var permissions = unitOfWork.GetRepository<Permission, int>();
            var permissionId = await permissions.InsertAsync(new Permission
            {
                Id = 0, Action = "smoke.read", Created = now
            });
            Require((await permissions.GetByIdAsync(permissionId))?.Action == "smoke.read",
                "Permission must map to Permissions.Action.");

            var ticketTypes = unitOfWork.GetRepository<TicketType, int>();
            var ticketTypeId = await ticketTypes.InsertAsync(new TicketType { Id = 0, Name = "Smoke type", Created = now });
            Require((await ticketTypes.GetByIdAsync(ticketTypeId))?.Name == "Smoke type",
                "TicketType must map to TicketTypes.");

            var ticketPriorities = unitOfWork.GetRepository<TicketPriority, int>();
            var ticketPriorityId = await ticketPriorities.InsertAsync(new TicketPriority { Id = 0, Name = "Smoke priority", Created = now });
            Require((await ticketPriorities.GetByIdAsync(ticketPriorityId))?.Name == "Smoke priority",
                "TicketPriority must map to TicketPriorities.");

            var ticketStatuses = unitOfWork.GetRepository<TicketStatus, int>();
            var ticketStatusId = await ticketStatuses.InsertAsync(new TicketStatus { Id = 0, Name = "Smoke status", Created = now });
            Require((await ticketStatuses.GetByIdAsync(ticketStatusId))?.Name == "Smoke status",
                "TicketStatus must map to TicketStatuses.");

            Require(await roles.DeleteByIdAsync(roleId), "Role delete must affect the migrated table.");
            Require(await roles.GetByIdAsync(roleId) is null, "A deleted role must not be returned by id.");

            unitOfWork.Commit();
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(path + suffix); } catch { /* best effort */ }
            }
        }

        Console.WriteLine("PASS: migrations produce entity-shaped tables and representative CRUD works with [Table] mappings and no non-column writes.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
