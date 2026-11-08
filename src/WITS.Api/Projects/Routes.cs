using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Common;
using WITS.Models;
using WITS.Services;
using WITS.Api.Common;

namespace WITS.Api.Projects;

public record CreateProjectRequest(string Name, string? Description);
public record UpdateProjectRequest(string Name, string? Description);
public record AddProjectMemberRequest(long UserId);

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

        group.MapPost("", CreateHandler)
            .WithName("Create project")
            .WithSummary("Create a project")
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("{id:long}", UpdateHandler)
            .WithName("Update project")
            .WithSummary("Update a project")
            .Produces<ProjectResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("{id:long}/members", AddMemberHandler)
            .WithName("Add project member")
            .WithSummary("Add a project member")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapDelete("{id:long}/members/{userId:long}", RemoveMemberHandler)
            .WithName("Remove project member")
            .WithSummary("Remove a project member")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("{id:long}", RetireHandler)
            .WithName("Retire project")
            .WithSummary("Soft-delete a project")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Ok<PagedResult<ProjectResponse>>> GetAllHandler(
        IProjectService service, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await service.GetAllAsync(page, pageSize, cancellationToken));

    private static async Task<Results<Ok<ProjectResponse>, ProblemHttpResult>> GetByIdHandler(
        IProjectService service, long id, CancellationToken cancellationToken = default)
    {
        var project = await service.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound($"Project {id} does not exist.", ApiErrorCodes.NotFound));
        }
        return TypedResults.Ok(project);
    }

    private static async Task<Ok<PagedResult<ProjectResponse>>> QueryHandler(
        IProjectService service, [FromBody] GenericFilter filter, CancellationToken cancellationToken = default) =>
        TypedResults.Ok(await service.QueryAsync(filter, cancellationToken));

    private static async Task<Results<Ok<ProjectResponse>, ProblemHttpResult>> CreateHandler(
        IProjectService service, [FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(ApiErrors.Validation("Project name is required.", ApiErrorCodes.ValidationFailed));
        }

        var project = await service.CreateAsync(request.Name, request.Description, cancellationToken);
        if (project is null)
        {
            return TypedResults.Problem(ApiErrors.Validation("Project could not be created.", ApiErrorCodes.ValidationFailed));
        }

        return TypedResults.Ok(project);
    }

    private static async Task<Results<Ok<ProjectResponse>, ProblemHttpResult>> UpdateHandler(
        IProjectService service, long id, [FromBody] UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(ApiErrors.Validation("Project name is required.", ApiErrorCodes.ValidationFailed));
        }

        var project = await service.UpdateAsync(id, request.Name, request.Description, cancellationToken);
        if (project is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Project does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.Ok(project);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> AddMemberHandler(
        IProjectService service, long id, [FromBody] AddProjectMemberRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.UserId <= 0)
        {
            return TypedResults.Problem(ApiErrors.Validation("User id is required.", ApiErrorCodes.ValidationFailed));
        }

        var added = await service.AddMemberAsync(id, request.UserId, cancellationToken);
        if (!added)
        {
            return TypedResults.Problem(ApiErrors.Validation("Member could not be added.", ApiErrorCodes.ValidationFailed));
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveMemberHandler(
        IProjectService service, long id, long userId, CancellationToken cancellationToken)
    {
        var removed = await service.RemoveMemberAsync(id, userId, cancellationToken);
        if (!removed)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Membership does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RetireHandler(
        IProjectService service, long id, CancellationToken cancellationToken)
    {
        var retired = await service.RetireAsync(id, cancellationToken);
        if (!retired)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Project does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.NoContent();
    }
}
