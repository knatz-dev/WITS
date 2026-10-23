using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Common;
using WITS.Models;
using WITS.Services;
using WITS.Api.Common;

namespace WITS.Api.Projects;

[Authorize]
public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        RouteGroupBuilder group = routeBuilder.MapGroup("project")
            .WithTags("Projects")
            .RequireAuthorization();
        group.MapGet("", GetAllHandler)
            .WithName("Get All projects")
            .WithSummary("List projects")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("{id:long}", GetByIdHandler)
            .WithName("Get project by id")
            .WithSummary("Get a project by id")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("query", QueryHandler)
            .WithName("Query projects")
            .WithSummary("Query projects")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<PagedResult<ProjectResponse>>> GetAllHandler(
        IProjectService service, int page = 1, int pageSize = 100) =>
        TypedResults.Ok(await service.GetAllAsync(page, pageSize));

    private static async Task<Results<Ok<ProjectResponse>, ProblemHttpResult>> GetByIdHandler(
        IProjectService service, long id)
    {
        var project = await service.GetByIdAsync(id);
        if (project is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound($"Project {id} does not exist.", ApiErrorCodes.NotFound));
        }
        return TypedResults.Ok(project);
    }

    private static async Task<Ok<PagedResult<ProjectResponse>>> QueryHandler(
        IProjectService service, [FromBody] GenericFilter filter) =>
        TypedResults.Ok(await service.QueryAsync(filter));
}
