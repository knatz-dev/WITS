using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Common;
using WITS.Models;
using WITS.Services;
using WITS.Api.Common;

namespace WITS.Api.Tickets;

public record CreateTicketRequest(string Summary, string? Description, int TypeId, int PriorityId, int StatusId, long ProjectId, long? Assignee, DateTime? DueDate);
public record UpdateTicketRequest(string Summary, string? Description, int TypeId, int PriorityId, int StatusId, long? Assignee, DateTime? DueDate, int RowVersion);
public record AssignTicketRequest(long? Assignee, int RowVersion);

[Authorize]
public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        RouteGroupBuilder ticketsGroup = routeBuilder.MapGroup("ticket")
            .WithTags("Tickets")
            .RequireAuthorization();
        ticketsGroup.MapGet("", GetAllHandler)
            .WithName("Get All tickets Unfiltered")
            .WithSummary("List tickets")
            .WithDescription("Returns one page of tickets. `page` is 1-based (default 1) and `pageSize` is bounded to 1-100 "
                             + "(values outside the range are rejected as 400). The response includes `totalRecords` and `totalPages`.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        ticketsGroup.MapGet("{id:long}", GetByIdHandler)
            .WithName("Get ticket by unique identifier")
            .WithSummary("Get a ticket by id")
            .WithDescription("Returns the ticket projection; storage-only fields are omitted. Unknown ids return 404.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        ticketsGroup.MapPost("query", QueryHandler)
            .WithName("Get All ticket with sorting pagination and filtering")
            .WithSummary("Query tickets")
            .WithDescription("Filters, sorts and pages tickets. `filters` maps a column name to `{value, operator}`. "
                             + "Unknown filter fields, operators, sort columns or sort orders return 400; page size is 1-100.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        ticketsGroup.MapPost("", CreateHandler)
            .WithName("Create ticket")
            .WithSummary("Create a ticket")
            .Produces<TicketResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        ticketsGroup.MapPut("{id:long}", UpdateHandler)
            .WithName("Update ticket")
            .WithSummary("Update a ticket")
            .Produces<TicketResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        ticketsGroup.MapDelete("{id:long}", DeleteHandler)
            .WithName("Delete ticket")
            .WithSummary("Soft-delete a ticket")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        ticketsGroup.MapPost("{id:long}/assign", AssignHandler)
            .WithName("Assign ticket")
            .WithSummary("Assign a ticket")
            .Produces<TicketResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        ticketsGroup.MapGet("type", TypesHandler)
            .WithName("Get ticket types")
            .WithSummary("List ticket types")
            .Produces<IReadOnlyList<CatalogueItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        ticketsGroup.MapGet("priority", PrioritiesHandler)
            .WithName("Get ticket priorities")
            .WithSummary("List ticket priorities")
            .Produces<IReadOnlyList<CatalogueItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        ticketsGroup.MapGet("status", StatusesHandler)
            .WithName("Get ticket statuses")
            .WithSummary("List ticket statuses")
            .Produces<IReadOnlyList<CatalogueItemResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<Ok<PagedResult<TicketResponse>>> QueryHandler(
        ITicketService ticketService, [FromBody] GenericFilter filter, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await ticketService.QueryAsync(filter, cancellationToken));
    }

    private static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> GetByIdHandler(
        ITicketService ticketService, long id, CancellationToken cancellationToken)
    {
        var ticket = await ticketService.GetByIdAsync(id, cancellationToken);
        if (ticket is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound($"Ticket {id} does not exist.", ApiErrorCodes.NotFound));
        }
        return TypedResults.Ok(ticket);
    }

    private static async Task<Ok<PagedResult<TicketResponse>>> GetAllHandler(
        ITicketService ticketService, int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        return TypedResults.Ok(await ticketService.GetAllAsync(page, pageSize, cancellationToken));
    }

    private static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> CreateHandler(
        ITicketService ticketService, [FromBody] CreateTicketRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Summary))
        {
            return TypedResults.Problem(ApiErrors.Validation("Ticket summary is required.", ApiErrorCodes.ValidationFailed));
        }

        var ticket = await ticketService.CreateAsync(request.Summary, request.Description, request.TypeId, request.PriorityId, request.StatusId, request.ProjectId, request.Assignee, request.DueDate, cancellationToken);
        if (ticket is null)
        {
            return TypedResults.Problem(ApiErrors.Validation("Ticket could not be created.", ApiErrorCodes.ValidationFailed));
        }

        return TypedResults.Ok(ticket);
    }

    private static async Task<Ok<IReadOnlyList<CatalogueItemResponse>>> TypesHandler(
        ITicketCatalogueService catalogueService, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalogueService.GetTypesAsync(cancellationToken));

    private static async Task<Ok<IReadOnlyList<CatalogueItemResponse>>> PrioritiesHandler(
        ITicketCatalogueService catalogueService, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalogueService.GetPrioritiesAsync(cancellationToken));

    private static async Task<Ok<IReadOnlyList<CatalogueItemResponse>>> StatusesHandler(
        ITicketCatalogueService catalogueService, CancellationToken cancellationToken) =>
        TypedResults.Ok(await catalogueService.GetStatusesAsync(cancellationToken));

    private static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> UpdateHandler(
        ITicketService ticketService, long id, [FromBody] UpdateTicketRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Summary))
        {
            return TypedResults.Problem(ApiErrors.Validation("Ticket summary is required.", ApiErrorCodes.ValidationFailed));
        }

        var ticket = await ticketService.UpdateAsync(id, request.Summary, request.Description, request.TypeId, request.PriorityId, request.StatusId, request.Assignee, request.DueDate, request.RowVersion, cancellationToken);
        if (ticket is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Ticket does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.Ok(ticket);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteHandler(
        ITicketService ticketService, long id, CancellationToken cancellationToken)
    {
        var deleted = await ticketService.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Ticket does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> AssignHandler(
        ITicketService ticketService, long id, [FromBody] AssignTicketRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return TypedResults.Problem(ApiErrors.Validation("Request body is required.", ApiErrorCodes.InvalidRequest));
        }

        var ticket = await ticketService.AssignAsync(id, request.Assignee, request.RowVersion, cancellationToken);
        if (ticket is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Ticket does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.Ok(ticket);
    }
}
