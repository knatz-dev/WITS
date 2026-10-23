using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using WITS.Api.Auth;
using WITS.Services;
using Xunit;

namespace WITS.Api.EndpointTests;

// The published contract must let a caller drive the supported flows: bearer auth on
// tickets, anonymous auth routes, and the routes/response shapes the handlers return.
public sealed class OpenApiContractTests
{
    private const string SigningKey = "EndpointTestsOnlySigningKey-32Bytes!";

    [Fact]
    public async Task PublishedContractCoversSupportedFlows()
    {
        await using var application = CreateHost();
        using var client = application.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("WITS API", root.GetProperty("info").GetProperty("title").GetString());

        JsonElement scheme = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());

        JsonElement paths = root.GetProperty("paths");
        foreach (string path in new[]
                 {
                     "/api/v1/auth/login", "/api/v1/auth/register", "/api/v1/auth/refresh", "/api/v1/auth/logout",
                     "/api/v1/ticket", "/api/v1/ticket/{id}", "/api/v1/ticket/query"
                 })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"OpenAPI is missing {path}.");
        }

        // Protected ticket operations cite the scheme; anonymous auth operations do not.
        JsonElement ticketSecurity = paths.GetProperty("/api/v1/ticket").GetProperty("get")
            .GetProperty("security")[0].GetProperty("Bearer");
        Assert.Equal(JsonValueKind.Array, ticketSecurity.ValueKind);
        Assert.False(paths.GetProperty("/api/v1/auth/login").GetProperty("post").TryGetProperty("security", out _),
            "Anonymous login must not require bearer authentication.");

        // The handlers declare the success and known-error responses a caller must handle.
        JsonElement getById = paths.GetProperty("/api/v1/ticket/{id}").GetProperty("get").GetProperty("responses");
        Assert.True(getById.TryGetProperty("200", out _), "Ticket lookup must document its 200 response.");
        Assert.True(getById.TryGetProperty("404", out _), "Ticket lookup must document its 404 response.");
        JsonElement loginResponses = paths.GetProperty("/api/v1/auth/login").GetProperty("post").GetProperty("responses");
        Assert.True(loginResponses.TryGetProperty("200", out _), "Login must document its 200 response.");
        Assert.True(loginResponses.TryGetProperty("401", out _), "Login must document its 401 response.");
    }

    private static WebApplicationFactory<Routes> CreateHost() =>
        new WebApplicationFactory<Routes>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
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
            });
        });
}
