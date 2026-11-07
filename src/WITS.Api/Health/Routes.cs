using Dapper;
using Microsoft.AspNetCore.Http.HttpResults;
using WITS.Api.Common;
using WITS.Data;
using WITS.Data.Contracts;

namespace WITS.Api.Health;

public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("health")
            .WithTags("Health")
            .AllowAnonymous();

        group.MapGet("live", LiveHandler)
            .WithName("Liveness")
            .WithSummary("Process liveness")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("ready", ReadyHandler)
            .WithName("Readiness")
            .WithSummary("Database and schema readiness")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult LiveHandler() =>
        TypedResults.Ok(new { status = "Healthy" });

    private static async Task<IResult> ReadyHandler(
        IDbConnectionFactory connectionFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Health.Readiness");
        try
        {
            using var connection = await connectionFactory.CreateConnectionAsync(cancellationToken);
            var probe = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1;", cancellationToken: cancellationToken));
            if (probe != 1)
            {
                throw new InvalidOperationException("Database probe did not return the expected result.");
            }

            // Schema/migration history must be present and readable without applying anything.
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM __Migrations;", cancellationToken: cancellationToken));
            if (count <= 0)
            {
                throw new InvalidOperationException("Migration history is missing or empty.");
            }

            return TypedResults.Ok(new { status = "Healthy" });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Readiness check failed.");
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}