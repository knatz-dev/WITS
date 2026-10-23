using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WITS.Common;
using WITS.Data;
using WITS.Data.Factory;
using WITS.Migrator.Migrations;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class ConstraintChecks
{
    private const string Password = "constraint-check-password";
    private const string SigningKey = "ConstraintChecksOnlySigningKey-32Bytes";
    private const string InsertUser = """
                                      INSERT INTO User
                                          (OrganisationId, FirstName, LastName, Username, EmailAddress, PasswordHash, PasswordSalt, Created)
                                      VALUES
                                          (@OrganisationId, 'Constraint', 'Check', @Username, @EmailAddress, 'hash', 'salt', @Created);
                                      """;

    public static async Task RunAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencode", $"wits-constraints-{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString();

        try
        {
            ApplyMigrations(connectionString);
            await CheckAccountUniquenessAsync(connectionString);
            await CheckRelationshipConstraintsAsync(connectionString);
            await CheckConcurrentRegistrationAsync(connectionString);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            {
                try { File.Delete(path + suffix); } catch { /* best effort */ }
            }
        }

        Console.WriteLine("PASS: canonical constraints reject duplicate account identifiers and invalid references, and concurrent duplicate registrations yield one winner.");
    }

    private static void ApplyMigrations(string connectionString)
    {
        var migrations = typeof(Migration).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .Select(type => (Migration)Activator.CreateInstance(type)!)
            .OrderBy(migration => migration.MigrationId);

        foreach (var migration in migrations)
        {
            migration.Apply(connectionString);
        }
    }

    private static async Task CheckAccountUniquenessAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        var userId = await InsertUserAsync(connection, 1, "Alice", "alice@example.com");

        var duplicateUsername = await ExpectConstraintAsync(connection, InsertUser,
            ("@OrganisationId", 1), ("@Username", "alice"), ("@EmailAddress", "other@example.com"), ("@Created", DateTime.UtcNow));
        Require(duplicateUsername.SqliteExtendedErrorCode == 2067,
            "Usernames must be unique case-insensitively.");

        var duplicateEmail = await ExpectConstraintAsync(connection, InsertUser,
            ("@OrganisationId", 1), ("@Username", "Other"), ("@EmailAddress", "ALICE@EXAMPLE.COM"), ("@Created", DateTime.UtcNow));
        Require(duplicateEmail.SqliteExtendedErrorCode == 2067,
            "Email addresses must be unique case-insensitively.");

        var invalidOrganisation = await ExpectConstraintAsync(connection, InsertUser,
            ("@OrganisationId", 999), ("@Username", "NoOrganisation"), ("@EmailAddress", "no-organisation@example.com"), ("@Created", DateTime.UtcNow));
        Require(invalidOrganisation.SqliteExtendedErrorCode == 787,
            "A user must reference an existing organisation.");

        Require(userId > 0, "The canonical User insert must return a generated key.");
    }

    private static async Task CheckRelationshipConstraintsAsync(string connectionString)
    {
        await using var connection = await OpenAsync(connectionString);
        var userId = await InsertUserAsync(connection, 1, "Member", "member@example.com");

        await ExecuteAsync(connection, "INSERT INTO Projects (Name, CreatedByUserId) VALUES ('Constraint project', @UserId);",
            ("@UserId", userId));
        var projectId = await ScalarAsync(connection, "SELECT last_insert_rowid();");

        await ExecuteAsync(connection,
            "INSERT INTO OrganizationMembers (OrganizationId, UserId, Role) VALUES (1, @UserId, 'Member');",
            ("@UserId", userId));
        var duplicateMember = await ExpectConstraintAsync(connection,
            "INSERT INTO OrganizationMembers (OrganizationId, UserId, Role) VALUES (1, @UserId, 'Member');",
            ("@UserId", userId));
        Require(duplicateMember.SqliteErrorCode == 19,
            "Organisation membership must be unique per organisation and user.");

        await ExecuteAsync(connection,
            "INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (@ProjectId, @UserId, 'Member');",
            ("@ProjectId", projectId), ("@UserId", userId));
        var duplicateProjectMember = await ExpectConstraintAsync(connection,
            "INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (@ProjectId, @UserId, 'Member');",
            ("@ProjectId", projectId), ("@UserId", userId));
        Require(duplicateProjectMember.SqliteErrorCode == 19,
            "Project membership must be unique per project and user.");

        var invalidProjectMember = await ExpectConstraintAsync(connection,
            "INSERT INTO ProjectMembers (ProjectId, UserId, Role) VALUES (999, @UserId, 'Member');",
            ("@UserId", userId));
        Require(invalidProjectMember.SqliteExtendedErrorCode == 787,
            "Project membership must reference an existing project.");

        await ExecuteAsync(connection, "INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, 1);", ("@UserId", userId));
        var duplicateRole = await ExpectConstraintAsync(connection,
            "INSERT INTO UserRoles (UserId, RoleId) VALUES (@UserId, 1);", ("@UserId", userId));
        Require(duplicateRole.SqliteErrorCode == 19,
            "A role grant must be unique per user and role.");
    }

    private static async Task CheckConcurrentRegistrationAsync(string connectionString)
    {
        var usernameRace = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => RegisterAsync(connectionString, "race-user", "race@example.com", 1)));
        Require(usernameRace.Count(result => result.Success) == 1
                && usernameRace.Where(result => !result.Success).All(result => result.Conflict),
            "Concurrent duplicate usernames must produce one registration and predictable conflicts.");

        var emailRace = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(index => RegisterAsync(connectionString, $"race-email-{index}", "race-email@example.com", 1)));
        Require(emailRace.Count(result => result.Success) == 1
                && emailRace.Where(result => !result.Success).All(result => result.Conflict),
            "Concurrent duplicate email addresses must produce one registration and predictable conflicts.");

        var normalized = await RegisterAsync(connectionString, "  trimmed-user  ", "  trimmed@example.com  ", 1);
        Require(normalized.Success, "Registration must normalize surrounding whitespace.");
        var duplicateNormalized = await RegisterAsync(connectionString, "trimmed-user", "other-trimmed@example.com", 1);
        Require(!duplicateNormalized.Success && duplicateNormalized.Conflict,
            "A normalized duplicate username must conflict.");

        var invalidOrganisation = await RegisterAsync(connectionString, "no-organisation", "no-organisation@example.com", 999);
        Require(!invalidOrganisation.Success && !invalidOrganisation.Conflict
                && invalidOrganisation.Message == "Organisation does not exist.",
            "Registration must reject an invalid organisation reference as a validation failure.");

        var login = await ValidateAsync(connectionString, "  trimmed-user  ", Password);
        Require(login.Success, "Login must ignore surrounding username whitespace.");
    }

    private static async Task<RegistrationResult> RegisterAsync(string connectionString, string username, string email, int organisationId)
    {
        using var unitOfWork = new UnitOfWork(new SQliteConnectionFactory(connectionString));
        return await CreateService(unitOfWork).RegisterUserAsync(new RegisterRequest
        {
            Username = username,
            FirstName = "Constraint",
            LastName = "Check",
            EmailAddress = email,
            OrganisationId = organisationId,
            Password = Password,
            TelephoneNos = "000"
        });
    }

    private static async Task<CredentialResult> ValidateAsync(string connectionString, string username, string password)
    {
        using var unitOfWork = new UnitOfWork(new SQliteConnectionFactory(connectionString));
        return await CreateService(unitOfWork).ValidateCredentialsAsync(username, password);
    }

    private static AuthService CreateService(IUnitOfWork unitOfWork) =>
        new(
            unitOfWork,
            Options.Create(new JwtSettings
            {
                SecurityKey = SigningKey,
                Issuer = "constraint-checks",
                Audience = "constraint-checks",
                ExpirationMinutes = 5
            }),
            NullLogger<AuthService>.Instance,
            new LoginAttemptTracker(TimeProvider.System),
            new RefreshTokenStore(TimeProvider.System));

    private static async Task<SqliteConnection> OpenAsync(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static async Task<long> InsertUserAsync(SqliteConnection connection, int organisationId, string username, string email)
    {
        await ExecuteAsync(connection, InsertUser,
            ("@OrganisationId", organisationId), ("@Username", username), ("@EmailAddress", email), ("@Created", DateTime.UtcNow));
        return await ScalarAsync(connection, "SELECT last_insert_rowid();");
    }

    private static async Task<SqliteException> ExpectConstraintAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            await ExecuteAsync(connection, sql, parameters);
        }
        catch (SqliteException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected the database to reject: {sql}");
    }

    private static async Task<int> ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
