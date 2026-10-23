// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Data.Factory;

namespace WITS.Seeder;

internal static class DbInitializer
{
    private const string AdminPassword = "Admin@2025!";
    private const string DemoPassword = "demo-password!";

    public static async Task InitializeAsync(SQliteConnectionFactory sqliteConnFactory)
    {
        ArgumentNullException.ThrowIfNull(sqliteConnFactory);

        using var uow = new UnitOfWork(sqliteConnFactory);
        uow.BeginTransaction();
        try
        {
            var witsOrganisationId = await EnsureOrganisationAsync(
                uow, "WITS", "Work Item Tracking System (WITS)", "WITS University");
            var demoOrganisationId = await EnsureOrganisationAsync(
                uow, "DEMO", "Demo", "Demo Organisation for testing purposes");

            await EnsureUserAsync(uow, witsOrganisationId, "admin", "Admin", "Strator",
                "admin@example.com", AdminPassword);
            await EnsureUserAsync(uow, demoOrganisationId, "demo", "Demo", "User",
                "demo@example.com", DemoPassword);

            uow.Commit();
        }
        catch
        {
            try
            {
                uow.Rollback();
            }
            catch (Exception rollbackException)
            {
                await Console.Error.WriteLineAsync($"Rollback after failed seeding also failed: {rollbackException.Message}");
            }

            // Rethrow so the entrypoint reports the seeding failure and exits nonzero.
            throw;
        }
    }

    private static async Task<int> EnsureOrganisationAsync(
        UnitOfWork uow, string code, string name, string description)
    {
        IGenericRepository<Organisation, int> repository = uow.GetRepository<Organisation, int>();
        var existing = await repository.QueryAsync(new GenericFilter(
            Filters: new Dictionary<string, FilterCriteria> { ["Code"] = new FilterCriteria(code) },
            PageSize: 1));

        if (existing.Items.Count == 1)
        {
            return existing.Items[0].Id;
        }

        return await repository.InsertAsync(new Organisation
        {
            Id = 0,
            Code = code,
            Name = name,
            Description = description,
            Created = DateTime.UtcNow
        });
    }

    private static async Task EnsureUserAsync(
        UnitOfWork uow, int organisationId, string username, string firstName, string lastName,
        string emailAddress, string password)
    {
        IGenericRepository<User, long> repository = uow.GetRepository<User, long>();
        var existing = await repository.QueryAsync(new GenericFilter(
            Filters: new Dictionary<string, FilterCriteria> { ["Username"] = new FilterCriteria(username) },
            PageSize: 1));

        if (existing.Items.Count == 1)
        {
            var user = existing.Items[0];
            if (user.OrganisationId != organisationId)
            {
                // Repair rows seeded before organisations were resolved by code.
                user.OrganisationId = organisationId;
                await repository.UpdateAsync(user);
            }

            return;
        }

        // Hash only on insert so reruns neither pay the PBKDF2 cost nor reset a changed password.
        var (hash, salt) = AuthHelper.HashPassword(password);
        await repository.InsertAsync(new User
        {
            Id = 0,
            OrganisationId = organisationId,
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            EmailAddress = emailAddress,
            PasswordHash = hash,
            PasswordSalt = salt,
            PhoneNumber = "123-456-7890",
            Created = DateTime.UtcNow
        });
    }
}
