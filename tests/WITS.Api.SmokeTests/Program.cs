using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WITS.Api.Auth;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Services;
using WITS.Api.SmokeTests;

// ASP.NET Core 9's test host discovers .sln files, but this repository uses .slnx.
[assembly: WebApplicationFactoryContentRoot("WITS.Api", "../../../../../src/WITS.Api", "WITS.Api.csproj", "0")]

await using var application = new WebApplicationFactory<Routes>()
    .WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:"
            }));
    });

using var client = application.CreateClient();
using var response = await client.GetAsync("/api/v1/ticket");
if (response.StatusCode != HttpStatusCode.Unauthorized)
{
    throw new InvalidOperationException($"Expected a protected ticket route, received {response.StatusCode}.");
}

using var scope = application.Services.CreateScope();
var services = scope.ServiceProvider;
var connectionFactory = services.GetRequiredService<IDbConnectionFactory>();
using var connection = await connectionFactory.CreateConnectionAsync();
if (connection.ConnectionString != "Data Source=:memory:")
{
    throw new InvalidOperationException("The connection factory did not use the configured database.");
}

var unitOfWork = services.GetRequiredService<IUnitOfWork>();
if (!ReferenceEquals(unitOfWork, services.GetRequiredService<IUnitOfWork>()))
{
    throw new InvalidOperationException("Unit of work must be shared within a request scope.");
}

_ = services.GetRequiredService<IAuthService>();
_ = services.GetRequiredService<ITicketService>();
_ = services.GetRequiredService<IGenericRepository<User, long>>();

using var otherScope = application.Services.CreateScope();
if (ReferenceEquals(unitOfWork, otherScope.ServiceProvider.GetRequiredService<IUnitOfWork>()))
{
    throw new InvalidOperationException("Unit of work must not be shared between request scopes.");
}

Console.WriteLine("PASS: API startup, configured SQLite factory, service resolution, and scoped unit of work.");

await DatabaseTargetChecks.RunAsync();
