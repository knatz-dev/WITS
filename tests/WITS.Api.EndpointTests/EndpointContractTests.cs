using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WITS.Api.Auth;
using WITS.Api.Common;
using Xunit;

namespace WITS.Api.EndpointTests;

// One case per endpoint behavior. Every mapped route must be referenced by at least one case.
public sealed class EndpointContractTests : IClassFixture<EndpointApiFixture>
{
    private const string LoginBody = """{"username":"smoke","password":"password"}""";
    private const string WrongPasswordBody = """{"username":"smoke","password":"wrong"}""";
    private const string NewUserBody = """
        {"username":"new.user","firstName":"New","lastName":"User","emailAddress":"new@example.com",
         "password":"password","created":"2025-01-01T00:00:00Z","telephoneNos":"000","organisation":"Organisation"}
        """;
    private const string DuplicateUserBody = """
        {"username":"existing","firstName":"Existing","lastName":"User","emailAddress":"existing@example.com",
         "password":"password","created":"2025-01-01T00:00:00Z","telephoneNos":"000","organisation":"Organisation"}
        """;
    private const string FilterBody = """{"pageNumber":1,"pageSize":10}""";

    private static readonly Case[] Cases =
    [
        new("login issues a token", "POST", "api/v1/auth/login", "/api/v1/auth/login",
            LoginBody, RequiresAuthentication: false, Authenticated: false, 200, "application/json",
            root => Assert.True(!string.IsNullOrEmpty(root.GetProperty("token").GetString()),
                "login must return a token.")),
        new("login rejects bad credentials", "POST", "api/v1/auth/login", "/api/v1/auth/login",
            WrongPasswordBody, false, false, 401, "application/problem+json"),
        new("register creates a user", "POST", "api/v1/auth/register", "/api/v1/auth/register",
            NewUserBody, false, false, 200, "application/json",
            root => Assert.True(root.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(root.GetString()),
                "register must return a message.")),
        new("register rejects a duplicate", "POST", "api/v1/auth/register", "/api/v1/auth/register",
            DuplicateUserBody, false, false, 409, "application/problem+json"),
        new("login rejects an unreadable body", "POST", "api/v1/auth/login", "/api/v1/auth/login",
            "{not-json", false, false, 400, "application/problem+json"),
        new("register rejects an unreadable body", "POST", "api/v1/auth/register", "/api/v1/auth/register",
            "{not-json", false, false, 400, "application/problem+json"),

        new("ticket list requires authentication", "GET", "api/v1/ticket", "/api/v1/ticket",
            null, true, false, 401, "application/problem+json"),
        new("ticket list returns a page", "GET", "api/v1/ticket", "/api/v1/ticket",
            null, true, true, 200, "application/json", page => AssertPage(page, expectedTotal: 1)),
        new("ticket by id requires authentication", "GET", "api/v1/ticket/{id:long}", "/api/v1/ticket/1",
            null, true, false, 401, "application/problem+json"),
        new("ticket by id returns the ticket", "GET", "api/v1/ticket/{id:long}", "/api/v1/ticket/1",
            null, true, true, 200, "application/json",
            root => Assert.True(root.GetProperty("id").GetInt64() == 1,
                "ticket by id must return the requested id.")),
        new("ticket by id reports a missing ticket", "GET", "api/v1/ticket/{id:long}", "/api/v1/ticket/999",
            null, true, true, 404, "application/problem+json"),
        new("ticket query requires authentication", "POST", "api/v1/ticket/query", "/api/v1/ticket/query",
            FilterBody, true, false, 401, "application/problem+json"),
        new("ticket query returns a page", "POST", "api/v1/ticket/query", "/api/v1/ticket/query",
            FilterBody, true, true, 200, "application/json", page => AssertPage(page, expectedTotal: 1)),
        new("ticket query rejects an unreadable body", "POST", "api/v1/ticket/query", "/api/v1/ticket/query",
            "{not-json", true, true, 400, "application/problem+json"),

        new("refresh issues tokens", "POST", "api/v1/auth/refresh", "/api/v1/auth/refresh",
            """{"refreshToken":"stub-refresh"}""", false, false, 200, "application/json",
            root => Assert.True(!string.IsNullOrEmpty(root.GetProperty("token").GetString())
                && !string.IsNullOrEmpty(root.GetProperty("refreshToken").GetString()),
                "refresh must return access and refresh tokens.")),
        new("refresh rejects an invalid token", "POST", "api/v1/auth/refresh", "/api/v1/auth/refresh",
            """{"refreshToken":"invalid"}""", false, false, 401, "application/problem+json"),
        new("logout revokes a refresh token", "POST", "api/v1/auth/logout", "/api/v1/auth/logout",
            """{"refreshToken":"stub-refresh"}""", false, false, 204, null),
        new("logout rejects an unreadable body", "POST", "api/v1/auth/logout", "/api/v1/auth/logout",
            "{not-json", false, false, 400, "application/problem+json")
    ];

    private readonly EndpointApiFixture _api;

    public EndpointContractTests(EndpointApiFixture api) => _api = api;

    public static TheoryData<string> CaseNames
    {
        get
        {
            var names = new TheoryData<string>();
            foreach (var testCase in Cases)
            {
                names.Add(testCase.Name);
            }
            return names;
        }
    }

    [Fact]
    public void Every_mapped_route_has_a_contract_case()
    {
        var registered = RegisteredRoutes(_api.Services);
        var covered = Cases.Select(testCase => Key(testCase.Method, testCase.Route))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Empty(registered.Except(covered, StringComparer.OrdinalIgnoreCase));
        Assert.Empty(covered.Except(registered, StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Endpoint_matches_contract(string name)
    {
        var testCase = Cases.Single(candidate => candidate.Name == name);
        using var request = new HttpRequestMessage(new HttpMethod(testCase.Method), testCase.Path);
        if (testCase.Body is not null)
        {
            request.Content = new StringContent(testCase.Body, Encoding.UTF8, "application/json");
        }
        if (testCase.Authenticated)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _api.Token);
        }

        using var response = await _api.Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True((int)response.StatusCode == testCase.ExpectedStatus,
            $"{testCase.Name}: expected {testCase.ExpectedStatus} but received {(int)response.StatusCode}: {body}");
        Assert.Equal(testCase.ExpectedMediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.True(!testCase.RequiresAuthentication || testCase.Authenticated || response.Headers.Contains("WWW-Authenticate"),
            $"{testCase.Name}: an anonymous request to a protected route must keep WWW-Authenticate.");

        if (string.IsNullOrEmpty(body))
        {
            return;
        }
        using var document = JsonDocument.Parse(body);
        if (testCase.ExpectedMediaType == "application/problem+json")
        {
            AssertProblem(testCase, (int)response.StatusCode, document.RootElement);
        }
        else
        {
            testCase.Check?.Invoke(document.RootElement);
        }
    }

    private static void AssertProblem(Case testCase, int status, JsonElement root)
    {
        Assert.True(root.TryGetProperty("status", out var actual) && actual.GetInt32() == status,
            $"{testCase.Name}: response status does not match the problem body.");
        Assert.True(root.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()),
            $"{testCase.Name}: response is missing the problem title.");
        Assert.True(root.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString()),
            $"{testCase.Name}: response is missing the problem detail.");
    }

    private static void AssertPage(JsonElement root, int expectedTotal)
    {
        Assert.True(root.GetProperty("items").GetArrayLength() == 1
            && root.GetProperty("totalRecords").GetInt32() == expectedTotal,
            "paged responses must carry the service's items and total.");
    }

    // The test host does not expose its endpoint data source, so replay Program.cs registration exactly.
    private static List<string> RegisteredRoutes(IServiceProvider services)
    {
        var builder = new CapturingRouteBuilder(services);
        var group = builder.MapGroup("api/v1/");
        foreach (var definition in typeof(Routes).Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(IEndpointDefinition)) && !type.IsAbstract && !type.IsInterface)
            .Select(type => (IEndpointDefinition)Activator.CreateInstance(type)!))
        {
            definition.Register(group);
        }

        return builder.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => Key(method, endpoint.RoutePattern.RawText ?? string.Empty)))
            .ToList();
    }

    private static string Key(string method, string route) => $"{method} {route.Trim('/')}";

    private sealed class CapturingRouteBuilder(IServiceProvider services) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = services;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed record Case(
        string Name,
        string Method,
        string Route,
        string Path,
        string? Body,
        bool RequiresAuthentication,
        bool Authenticated,
        int ExpectedStatus,
        string? ExpectedMediaType,
        Action<JsonElement>? Check = null);
}
