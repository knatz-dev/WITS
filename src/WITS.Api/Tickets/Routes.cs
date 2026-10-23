using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Common;
using WITS.Models;
using WITS.Services;
using WITS.Api.Common;

namespace WITS.Api.Tickets;

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
    }

    private static async Task<Ok<PagedResult<TicketResponse>>> QueryHandler(
        ITicketService ticketService, [FromBody] GenericFilter filter)
    {
        return TypedResults.Ok(await ticketService.QueryAsync(filter));
    }

    private static async Task<Results<Ok<TicketResponse>, ProblemHttpResult>> GetByIdHandler(
        ITicketService ticketService, long id)
    {
        var ticket = await ticketService.GetByIdAsync(id);
        if (ticket is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound($"Ticket {id} does not exist."));
        }
        return TypedResults.Ok(ticket);
    }

    private static async Task<Ok<PagedResult<TicketResponse>>> GetAllHandler(
        ITicketService ticketService, int page = 1, int pageSize = 100)
    {
        return TypedResults.Ok(await ticketService.GetAllAsync(page, pageSize));
    }
}
