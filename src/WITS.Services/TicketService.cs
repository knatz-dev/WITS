using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using EntityTicket = WITS.Data.Entity.Ticket;
using ModelTicket = WITS.Models.Ticket;
using WITS.Models;

namespace WITS.Services;

public interface ITicketService
{
    Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize);
    Task<TicketResponse?> GetByIdAsync(long id);
    Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter);
}

public class TicketService : ITicketService
{
    private readonly IGenericRepository<EntityTicket, long> _repository;

    public TicketService(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.GetRepository<EntityTicket, long>();
    }

    public async Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize)
    {
        var result = await _repository.GetAllAsync(page, pageSize);
        return new PagedResult<TicketResponse>(result.Items.Select(ToResponse).ToList(), result.TotalRecords, result.PageNumber, result.PageSize);
    }

    public async Task<TicketResponse?> GetByIdAsync(long id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter)
    {
        var result = await _repository.QueryAsync(filter);
        return new PagedResult<TicketResponse>(result.Items.Select(ToResponse).ToList(), result.TotalRecords, result.PageNumber, result.PageSize);
    }

    private static TicketResponse ToResponse(EntityTicket entity)
    {
        var model = new ModelTicket
        {
            Id = entity.Id,
            IsDeleted = entity.IsDeleted,
            Created = entity.Created,
            LastModified = entity.LastModified,
            Summary = entity.Title,
            Description = entity.Description,
            TypeId = entity.TypeId,
            StatusId = entity.StatusId,
            PriorityId = entity.PriorityId,
            DateCreated = entity.Created,
            DateResolved = entity.DueDate ?? entity.Created,
            LoggedByUsername = string.Empty,
            Reference = $"TICKET-{entity.Id}",
            NextAction = string.Empty,
            Assignee = entity.AssignedToUserId,
            ReportedByUsername = string.Empty,
            ProjectId = (int)entity.ProjectId,
            ProjectCode = string.Empty
        };
        return TicketResponse.From(model);
    }
}
