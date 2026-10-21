using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WITS.Common;
using WITS.Data;
using WITS.Data.Common;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class IdentityClaimsChecks
{
    private const string Password = "correct horse battery staple";

    public static async Task RunAsync()
    {
        var settings = new JwtSettings
        {
            SecurityKey = StartupConfigurationChecks.SigningKey,
            Issuer = "smoke-issuer",
            Audience = "smoke-audience",
            ExpirationMinutes = 5
        };
        var (hash, salt) = AuthHelper.HashPassword(Password);
        var account = new User
        {
            Id = 42,
            Username = "jane.doe",
            FirstName = "Jane",
            LastName = "Doe",
            EmailAddress = "jane.doe@example.com",
            OrganisationId = 7,
            PasswordHash = hash,
            PasswordSalt = salt
        };

        var authService = new AuthService(new StubUnitOfWork(account), Options.Create(settings));
        var issued = await authService.ValidateCredentialsAsync(account.Username, Password);
        if (!issued.Success || string.IsNullOrEmpty(issued.Token))
        {
            throw new InvalidOperationException("Valid credentials must issue a token.");
        }

        var principal = new JwtSecurityTokenHandler().ValidateToken(issued.Token, ValidationParameters(settings), out _);
        Require(principal.Identity?.IsAuthenticated == true, "The issued token must authenticate.");
        Require(principal.Identity?.Name == account.Username, "The name claim must resolve to the username.");
        Require(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value == "42",
            "The token must carry a stable subject identifying the account.");
        Require(principal.FindFirst(ClaimTypes.Email)?.Value == account.EmailAddress,
            "The email claim must carry the account address.");
        Require(principal.FindFirst("OrganisationId")?.Value == account.OrganisationId.ToString(),
            "The organisation claim must use organisation naming and the account's organisation.");
        Require(principal.FindFirst(ClaimTypes.Role) == null,
            "No role may be issued until real role grants are read.");
        Require(!principal.Claims.Any(claim => claim.Value == account.FullName),
            "Display names must never appear anywhere in a token.");

        var rejected = await authService.ValidateCredentialsAsync(account.Username, $"{Password}!");
        Require(!rejected.Success && rejected.Token is null, "A wrong password must not issue a token.");

        Console.WriteLine("PASS: tokens carry a stable subject and account claims, with no display-name roles.");
    }

    private static TokenValidationParameters ValidationParameters(JwtSettings settings) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = settings.Issuer,
        ValidAudience = settings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecurityKey))
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class StubUnitOfWork(User account) : IUnitOfWork
    {
        public IGenericRepository<T, TId> GetRepository<T, TId>() where T : BaseEntity<TId> =>
            (IGenericRepository<T, TId>)(object)new StubUserRepository(account);

        public void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
            throw new NotSupportedException("The stub unit of work does not manage transactions.");

        public void Commit() => throw new NotSupportedException("The stub unit of work does not manage transactions.");

        public void Rollback() => throw new NotSupportedException("The stub unit of work does not manage transactions.");

        public void Dispose() => GC.SuppressFinalize(this);
    }

    private sealed class StubUserRepository(User account) : IGenericRepository<User, long>
    {
        public Task<PagedResult<User>> QueryAsync(GenericFilter filter)
        {
            var matches = filter.Filters is not null
                && filter.Filters.TryGetValue("Username", out var criteria)
                && Equals(criteria.Value, account.Username);
            return Task.FromResult(matches
                ? new PagedResult<User>([account], 1, filter.PageNumber, filter.PageSize)
                : new PagedResult<User>([], 0, filter.PageNumber, filter.PageSize));
        }

        public Task<PagedResult<User>> GetAllAsync(int pageNumber = 1, int pageSize = 100) =>
            throw new NotImplementedException();

        public Task<User?> GetByIdAsync(long id) => throw new NotImplementedException();

        public Task<int> InsertAsync(User model) => throw new NotImplementedException();

        public Task<bool> UpdateAsync(User model) => throw new NotImplementedException();

        public Task<bool> DeleteByIdAsync(long id) => throw new NotImplementedException();

        public Task<PagedResult<User>> SearchAsync(SearchParams searchParams) => throw new NotImplementedException();
    }
}
