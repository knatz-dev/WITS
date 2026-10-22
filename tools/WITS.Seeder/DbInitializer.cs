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

    public static void Initialize(SQliteConnectionFactory sqliteConnFactory)
    {
        ArgumentNullException.ThrowIfNull(sqliteConnFactory);

        using var uow = new UnitOfWork(sqliteConnFactory);
        uow.BeginTransaction();
        try
        {
            SeedOrganisations(uow);
            SeedUsers(uow);
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
                Console.Error.WriteLine($"Rollback after failed seeding also failed: {rollbackException.Message}");
            }

            // Rethrow so the entrypoint reports the seeding failure and exits nonzero.
            throw;
        }
    }

    private static void SeedOrganisations(UnitOfWork uow)
    {
        IGenericRepository<Organisation, int> orgRepository = uow.GetRepository<Organisation, int>();

        var organisationsToSeed = new List<Organisation>
        {
            new Organisation
            {
                Id = 1,
                Code = "WITS",
                Name = "Work Item Tracking System (WITS)",
                Description = "WITS University",
                Created = DateTime.UtcNow,
                IsDeleted = false
            },
            new Organisation
            {
                Id = 2,
                Code = "DEMO",
                Name = "Demo",
                Description = "Demo Organisation for testing purposes",
                Created = DateTime.UtcNow,
                IsDeleted = false
            }
        };
        foreach (var org in organisationsToSeed)
        {
            orgRepository.InsertAsync(org).Wait();
        }

    }

    public static void SeedUsers(UnitOfWork uow)
    {
        IGenericRepository<User, long> userRepository = uow.GetRepository<User, long>();

        var (adminHash, adminSalt) = AuthHelper.HashPassword("Admin@2025!");
        var (demoHash, demoSalt) = AuthHelper.HashPassword("demo-password!");

        var usersToSeed = new List<User>
        {
            new User
            {
                Id = 1,
                Username = "admin",
                FirstName = "Admin",
                LastName = "Strator",
                EmailAddress = "admin@example.com",
                PasswordHash = adminHash,
                PasswordSalt = adminSalt,
                Created = DateTime.UtcNow,
                OrganisationId = 1,
                PhoneNumber = "123-456-7890",
            },
            new User
            {
                Id = 2,
                Username = "demo",
                FirstName = "Demo",
                LastName = "User",
                EmailAddress = "demo@example.com",
                PasswordHash = demoHash,
                PasswordSalt = demoSalt,
                Created = DateTime.UtcNow,
                OrganisationId = 2,
                PhoneNumber = "123-456-7890",
            }
        };

        foreach (var user in usersToSeed)
        {
            userRepository.InsertAsync(user).Wait();
        }
    }
}