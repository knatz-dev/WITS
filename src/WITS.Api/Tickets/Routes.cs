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
            .WithName("Get All tickets Unfiltered");
        ticketsGroup.MapGet("{id:long}", GetByIdHandler)
            .WithName("Get ticket by unique identifier");
        ticketsGroup.MapPost("query", QueryHandler)
            .WithName("Get All ticket with sorting pagination and filtering");
    }

    private static async Task<Results<Ok<PagedResult<Ticket>>, NoContent>> QueryHandler(
        ITicketService ticketService, [FromBody] GenericFilter filter)
    {
        PagedResult<Ticket> ticket = await ticketService.QueryAsync(filter);
        return ticket != null ? TypedResults.Ok(ticket) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<Ticket>, NotFound>> GetByIdHandler(
        ITicketService ticketService, long id)
    {
        var ticket = await ticketService.GetByIdAsync(id);
        return ticket != null ? TypedResults.Ok(ticket) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<PagedResult<Ticket>>, NoContent>> GetAllHandler(
        ITicketService ticketService, int page = 1, int pageSize = 100)
    {
        var tickets = await ticketService.GetAllAsync(page, pageSize);
        return tickets != null ? TypedResults.Ok(tickets) : TypedResults.NoContent();
    }
}
