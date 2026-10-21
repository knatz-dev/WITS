using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using WITS.Api.Auth;
using WITS.Common;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class ErrorResponseChecks
{
    public static async Task RunAsync()
    {
        await using var realHost = CreateHost();
        using var realClient = realHost.CreateClient();
        var token = IssueToken(realHost);

        using (var anonymous = await realClient.GetAsync("/api/v1/ticket"))
        {
            await ExpectProblemAsync(anonymous, StatusCodes.Status401Unauthorized, "unauthenticated request");
            if (!anonymous.Headers.TryGetValues("WWW-Authenticate", out _))
            {
                throw new InvalidOperationException("A 401 response must keep its WWW-Authenticate header.");
            }
        }

        using (var failing = await AuthorizedGetAsync(realClient, token, "/api/v1/ticket"))
        {
            await ExpectProblemAsync(failing, StatusCodes.Status500InternalServerError, "unhandled service failure",
                "NotImplementedException", "stackTrace");
        }

        await using var stubHost = CreateHost(services =>
        {
            services.Replace(ServiceDescriptor.Scoped<IAuthService, StubAuthService>());
            services.Replace(ServiceDescriptor.Scoped<ITicketService, StubTicketService>());
        });
        using var stubClient = stubHost.CreateClient();

        using (var login = await stubClient.PostAsync("/api/v1/auth/login", Json(new LoginRequest("smoke", "wrong"))))
        {
            await ExpectProblemAsync(login, StatusCodes.Status401Unauthorized, "rejected credentials");
        }

        using (var duplicate = await stubClient.PostAsync("/api/v1/auth/register", Json(NewUser("duplicate"))))
        {
            await ExpectProblemAsync(duplicate, StatusCodes.Status409Conflict, "duplicate registration");
        }

        using (var rejected = await stubClient.PostAsync("/api/v1/auth/register", Json(NewUser("new"))))
        {
            await ExpectProblemAsync(rejected, StatusCodes.Status400BadRequest, "registration validation failure");
        }

        using (var missing = await AuthorizedGetAsync(stubClient, token, "/api/v1/ticket/999"))
        {
            await ExpectProblemAsync(missing, StatusCodes.Status404NotFound, "missing resource");
        }

        using (var malformed = await realClient.PostAsync(
                   "/api/v1/auth/login", new StringContent("{not-json", Encoding.UTF8, "application/json")))
        {
            await ExpectProblemAsync(malformed, StatusCodes.Status400BadRequest, "unreadable request body");
        }

        var forbidden = await InvokeForbiddenAsync(realHost);
        CheckProblem(StatusCodes.Status403Forbidden, forbidden.StatusCode, forbidden.MediaType, forbidden.Body,
            "authorization refusal", []);

        Console.WriteLine("PASS: 400/401/403/404/409/500 answers share one ProblemDetails shape without leaking internals.");
    }

    private static WebApplicationFactory<Routes> CreateHost(Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
            ["JwtSettings:Issuer"] = "smoke-issuer",
            ["JwtSettings:Audience"] = "smoke-audience",
            ["JwtSettings:SecurityKey"] = StartupConfigurationChecks.SigningKey,
            ["JwtSettings:ExpirationMinutes"] = "5"
        };
        return new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            if (configureServices != null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });
    }

    private static async Task<(int StatusCode, string MediaType, string Body)> InvokeForbiddenAsync(
        WebApplicationFactory<Routes> host)
    {
        var events = host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).Events;
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = body } };
        context.RequestServices = host.Services;
        await events.OnForbidden(new ForbiddenContext(
            context, new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler)), new JwtBearerOptions()));
        body.Position = 0;
        using var reader = new StreamReader(body, Encoding.UTF8);
        return (context.Response.StatusCode, context.Response.ContentType ?? string.Empty, await reader.ReadToEndAsync());
    }

    private static string IssueToken(WebApplicationFactory<Routes> host)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(new UserTokenModel
        {
            Id = 1,
            Username = "smoke",
            EmailAddress = "smoke@example.com",
            OrganisationId = 1
        });
    }

    private static async Task<HttpResponseMessage> AuthorizedGetAsync(HttpClient client, string token, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json");

    private static RegisterRequest NewUser(string username) => new()
    {
        Username = username,
        FirstName = "First",
        LastName = "Last",
        EmailAddress = $"{username}@example.com",
        OrganisationId = 1,
        Password = "password",
        Created = DateTime.UtcNow,
        TelephoneNos = "000",
        Organisation = "Organisation"
    };

    private static async Task ExpectProblemAsync(HttpResponseMessage response, int expectedStatus, string context, params string[] absent)
    {
        CheckProblem(expectedStatus, (int)response.StatusCode, response.Content.Headers.ContentType?.MediaType,
            await response.Content.ReadAsStringAsync(), context, absent);
    }

    private static void CheckProblem(
        int expectedStatus, int actualStatus, string? mediaType, string body, string context, string[] absent)
    {
        var actualMediaType = mediaType?.Split(';')[0].Trim();
        if (actualStatus != expectedStatus || actualMediaType != "application/problem+json")
        {
            throw new InvalidOperationException(
                $"{context}: expected {expectedStatus} application/problem+json but received {actualStatus} {actualMediaType} with body '{body}'.");
        }

        var leaked = absent.FirstOrDefault(fragment => body.Contains(fragment, StringComparison.OrdinalIgnoreCase));
        if (leaked != null)
        {
            throw new InvalidOperationException($"{context}: response leaked '{leaked}': {body}");
        }

        using var problem = JsonDocument.Parse(body);
        var root = problem.RootElement;
        if (!root.TryGetProperty("status", out var status) || status.GetInt32() != expectedStatus
            || !root.TryGetProperty("title", out var title) || string.IsNullOrWhiteSpace(title.GetString())
            || !root.TryGetProperty("detail", out var detail) || string.IsNullOrWhiteSpace(detail.GetString()))
        {
            throw new InvalidOperationException($"{context}: response is missing status/title/detail: {body}");
        }
    }

    private sealed class StubAuthService : IAuthService
    {
        public string GenerateToken(UserTokenModel userToken) => throw new NotImplementedException();

        public Task<CredentialResult> ValidateCredentialsAsync(string username, string password) =>
            Task.FromResult(new CredentialResult(false, null, "Invalid credentials"));

        public Task<RegistrationResult> RegisterUserAsync(RegisterRequest user) =>
            Task.FromResult(user.Username == "duplicate"
                ? new RegistrationResult(false, "Username already exists", Conflict: true)
                : new RegistrationResult(false, "Password cannot be empty"));
    }

    private sealed class StubTicketService : ITicketService
    {
        public Task<PagedResult<Ticket>> GetAllAsync(int page, int pageSize) =>
            Task.FromResult(new PagedResult<Ticket>([], 0, page, pageSize));

        public Task<Ticket> GetByIdAsync(long id) => Task.FromResult<Ticket>(null!);

        public Task<PagedResult<Ticket>> QueryAsync(GenericFilter filter) =>
            Task.FromResult(new PagedResult<Ticket>([], 0, 1, 100));
    }
}
