namespace WITS.Models;

public class Ticket
{
    public long Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastModified { get; set; }
    public required string Summary { get; set; }
    public string? Description { get; set; }
    public int TypeId { get; set; }
    public int StatusId { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime DateResolved { get; set; }
    public int PriorityId { get; set; }
    public required string LoggedByUsername { get; set; }
    public required string Reference { get; set; }
    public required string NextAction { get; set; }
    public long? Assignee { get; set; }
    public required string ReportedByUsername { get; set; }
    public string? ContactEmail { get; set; }
    public string? Details { get; set; }
    public int ProjectId { get; set; }
    public required string ProjectCode { get; set; }
    public string TicketRef => $"{ProjectCode}-{Id:D8}";
}

// API-facing ticket shape. Storage-only fields (soft-delete flag, audit timestamps) are omitted.
public sealed record TicketResponse(
    long Id,
    string Reference,
    string TicketRef,
    string Summary,
    string? Description,
    int TypeId,
    int StatusId,
    int PriorityId,
    DateTime DateCreated,
    DateTime DateResolved,
    string LoggedByUsername,
    string ReportedByUsername,
    long? Assignee,
    string? ContactEmail,
    string? Details,
    int ProjectId,
    string ProjectCode,
    string NextAction)
{
    public static TicketResponse From(Ticket ticket) => new(
        ticket.Id,
        ticket.Reference,
        ticket.TicketRef,
        ticket.Summary,
        ticket.Description,
        ticket.TypeId,
        ticket.StatusId,
        ticket.PriorityId,
        ticket.DateCreated,
        ticket.DateResolved,
        ticket.LoggedByUsername,
        ticket.ReportedByUsername,
        ticket.Assignee,
        ticket.ContactEmail,
        ticket.Details,
        ticket.ProjectId,
        ticket.ProjectCode,
        ticket.NextAction);
}
