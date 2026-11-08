using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WITS.Api.Auth;
using WITS.Common;
using WITS.Models;
using WITS.Services;
using Xunit;

// ASP.NET Core 9's test host discovers .sln files, but this repository uses .slnx.
[assembly: WebApplicationFactoryContentRoot("WITS.Api", "../../../../../src/WITS.Api", "WITS.Api.csproj", "0")]

namespace WITS.Api.EndpointTests;

public sealed class EndpointApiFixture : IAsyncLifetime
{
    private const string SigningKey = "EndpointTestsOnlySigningKey-32Bytes!";
    private readonly WebApplicationFactory<Routes> _application = CreateHost();

    public HttpClient Client { get; private set; } = null!;
    public string Token { get; private set; } = string.Empty;
    public IServiceProvider Services => _application.Services;

    public Task InitializeAsync()
    {
        Client = _application.CreateClient();
        Token = IssueToken(_application);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _application.DisposeAsync();
    }

    private static WebApplicationFactory<Routes> CreateHost() =>
        new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                    ["JwtSettings:Issuer"] = "endpoint-tests-issuer",
                    ["JwtSettings:Audience"] = "endpoint-tests-audience",
                    ["JwtSettings:SecurityKey"] = SigningKey,
                    ["JwtSettings:ExpirationMinutes"] = "5"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IAuthService, StubAuthService>());
                services.Replace(ServiceDescriptor.Scoped<ITicketService, StubTicketService>());
                services.Replace(ServiceDescriptor.Scoped<IProjectService, StubProjectService>());
                services.Replace(ServiceDescriptor.Scoped<ITicketCatalogueService, StubTicketCatalogueService>());
                services.Replace(ServiceDescriptor.Scoped<IProfileService, StubProfileService>());
            });
        });

    // Authorization only needs a correctly signed token; auth cases exercise the stub service.
    private static string IssueToken(WebApplicationFactory<Routes> application)
    {
        var settings = application.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "1"),
                new Claim(ClaimTypes.Name, "smoke"),
                new Claim(ClaimTypes.Email, "smoke@example.com")
            ]),
            Expires = DateTime.UtcNow.AddMinutes(1),
            Issuer = settings.Issuer,
            Audience = settings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecurityKey)), SecurityAlgorithms.HmacSha256Signature)
        }));
    }
}

internal sealed class StubAuthService : IAuthService
{
    public string GenerateToken(UserTokenModel userToken) => "stub-token";

    public Task<CredentialResult> ValidateCredentialsAsync(string username, string password) =>
        Task.FromResult(username == "smoke" && password == "password"
            ? new CredentialResult(true, "stub-token", "stub-refresh")
            : new CredentialResult(false, null, null, "Invalid credentials"));

    public Task<RefreshResult> RefreshAsync(string refreshToken) =>
        Task.FromResult(refreshToken == "stub-refresh"
            ? new RefreshResult(true, "stub-token", "stub-refresh-2")
            : new RefreshResult(false, null, null, "Invalid refresh token"));

    public void RevokeRefreshToken(string refreshToken) { }
    public void RevokeAllRefreshTokens(long userId) { }

    public Task<RegistrationResult> RegisterUserAsync(RegisterRequest user, CancellationToken cancellationToken = default) =>
        Task.FromResult(user.Username == "existing"
            ? new RegistrationResult(false, "Username already exists", Conflict: true)
            : new RegistrationResult(true, "User registered successfully with id 17.", UserId: 17));

    public Task<PasswordChangeResult> ChangePasswordAsync(long userId, string currentPassword, string newPassword, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PasswordChangeResult(true));
    public Task<bool> SetAccountLockedAsync(long userId, bool locked, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}

internal sealed class StubTicketService : ITicketService
{
    private static readonly TicketResponse Ticket = TicketResponse.From(new Ticket
    {
        Id = 1,
        Summary = "Sample ticket",
        LoggedByUsername = "smoke",
        ReportedByUsername = "smoke",
        Reference = "WITS-1",
        NextAction = "None",
        ProjectCode = "WITS",
        // Unspecified mirrors a timestamp read back from SQLite; the API must still send it as UTC.
        DateCreated = new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Unspecified),
        DateResolved = new DateTime(2026, 10, 23, 10, 15, 0, DateTimeKind.Utc)
    });

    public Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<TicketResponse>([Ticket], 1, page, pageSize));

    public Task<TicketResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(id == Ticket.Id ? Ticket : null);

    public Task<TicketResponse?> CreateAsync(string summary, string? description, int typeId, int priorityId, int statusId, long projectId, long? assignee, DateTime? dueDate, CancellationToken cancellationToken = default) =>
        Task.FromResult<TicketResponse?>(Ticket);

    public Task<TicketResponse?> UpdateAsync(long id, string summary, string? description, int typeId, int priorityId, int statusId, long? assignee, DateTime? dueDate, int rowVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == Ticket.Id ? Ticket : null);

    public Task<TicketResponse?> AssignAsync(long id, long? assignee, int rowVersion, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == Ticket.Id ? Ticket : null);

    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == Ticket.Id);

    public Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<TicketResponse>([Ticket], 1, filter.PageNumber, filter.PageSize));
}

internal sealed class StubProjectService : IProjectService
{
    private static readonly ProjectResponse Project = new(
        1,
        "Sample project",
        "Description",
        1,
        1,
        new DateTime(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc),
        null);

    public Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<ProjectResponse>([Project], 1, page, pageSize));

    public Task<ProjectResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(id == Project.Id ? Project : null);

    public Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<ProjectResponse>([Project], 1, filter.PageNumber, filter.PageSize));

    public Task<ProjectResponse?> CreateAsync(string name, string? description, CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectResponse?>(Project);

    public Task<ProjectResponse?> UpdateAsync(long id, string name, string? description, CancellationToken cancellationToken = default) =>
        Task.FromResult(id == Project.Id ? Project : null);

    public Task<bool> AddMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public Task<bool> RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(userId != 999);
}

internal sealed class StubTicketCatalogueService : ITicketCatalogueService
{
    public Task<IReadOnlyList<CatalogueItemResponse>> GetTypesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogueItemResponse>>([new(1, "Bug")]);
    public Task<IReadOnlyList<CatalogueItemResponse>> GetPrioritiesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogueItemResponse>>([new(1, "High")]);
    public Task<IReadOnlyList<CatalogueItemResponse>> GetStatusesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CatalogueItemResponse>>([new(1, "Open")]);
}

internal sealed class StubProfileService : IProfileService
{
    private static readonly ProfileResponse Profile = new(1, "smoke", "smoke@example.com", "Smoke", "User", null, null, 1);

    public Task<ProfileResponse?> GetAsync(long userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(userId == 1 ? Profile : null);

    public Task<ProfileUpdateResult> UpdateAsync(long userId, UpdateProfileRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(userId == 1
            ? new ProfileUpdateResult(true, Profile: Profile)
            : new ProfileUpdateResult(false, NotFound: true));
}
