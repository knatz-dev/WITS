using System.Reflection;
using Dapper;
using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using EntityTicket = WITS.Data.Entity.Ticket;
using ModelTicket = WITS.Models.Ticket;
using WITS.Models;

namespace WITS.Services;

public interface ITicketService
{
    Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<TicketResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default);
    Task<TicketResponse?> CreateAsync(string summary, string? description, int typeId, int priorityId, int statusId, long projectId, long? assignee, DateTime? dueDate, CancellationToken cancellationToken = default);
    Task<TicketResponse?> UpdateAsync(long id, string summary, string? description, int typeId, int priorityId, int statusId, long? assignee, DateTime? dueDate, int rowVersion, CancellationToken cancellationToken = default);
    Task<TicketResponse?> AssignAsync(long id, long? assignee, int rowVersion, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}

public class TicketService : ITicketService
{
    private readonly IGenericRepository<EntityTicket, long> _repository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public TicketService(IUnitOfWork unitOfWork, IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _repository = unitOfWork.GetRepository<EntityTicket, long>();
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count == 0)
        {
            return new PagedResult<TicketResponse>([], 0, page, pageSize);
        }

        var all = await _repository.GetAllAsync(1, 100, cancellationToken);
        var filtered = all.Items.Where(ticket => allowed.Contains(ticket.ProjectId)).Select(ToResponse).ToList();
        var total = filtered.Count;
        var paged = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<TicketResponse>(paged, total, page, pageSize);
    }

    public async Task<TicketResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null || (allowed.Count > 0 && !allowed.Contains(entity.ProjectId)))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        return ToResponse(entity);
    }

    public async Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count == 0)
        {
            return new PagedResult<TicketResponse>([], 0, filter.PageNumber, filter.PageSize);
        }

        var all = await _repository.GetAllAsync(1, 100, cancellationToken);
        var candidates = all.Items.Where(ticket => allowed.Contains(ticket.ProjectId)).ToList();

        if (filter.Filters is not null)
        {
            foreach (var (property, criteria) in filter.Filters)
            {
                candidates = candidates.Where(ticket => MatchesFilter(ticket, property, criteria)).ToList();
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.SortColumn))
        {
            var property = typeof(EntityTicket).GetProperty(filter.SortColumn, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
            {
                throw new ArgumentException($"Unknown sort column '{filter.SortColumn}'.", nameof(filter));
            }

            var descending = string.Equals(filter.SortOrder, "DESC", StringComparison.OrdinalIgnoreCase);
            candidates = descending
                ? candidates.OrderByDescending(ticket => property.GetValue(ticket)).ToList()
                : candidates.OrderBy(ticket => property.GetValue(ticket)).ToList();
        }

        var total = candidates.Count;
        var paged = candidates
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(ToResponse)
            .ToList();

        return new PagedResult<TicketResponse>(paged, total, filter.PageNumber, filter.PageSize);
    }

    public async Task<TicketResponse?> CreateAsync(string summary, string? description, int typeId, int priorityId, int statusId, long projectId, long? assignee, DateTime? dueDate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var userId = _currentUser.UserId;
        if (userId is null)
        {
            return null;
        }

        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count > 0 && !allowed.Contains(projectId))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        var ticket = new EntityTicket
        {
            Id = 0,
            Title = summary.Trim(),
            Description = description?.Trim(),
            TypeId = typeId,
            PriorityId = priorityId,
            StatusId = statusId,
            ProjectId = projectId,
            CreatedByUserId = userId.Value,
            AssignedToUserId = assignee,
            DueDate = dueDate,
            Created = DateTime.UtcNow
        };

        var id = await _repository.InsertAsync(ticket, cancellationToken);
        ticket.Id = id;
        return ToResponse(ticket);
    }

    public async Task<TicketResponse?> UpdateAsync(long id, string summary, string? description, int typeId, int priorityId, int statusId, long? assignee, DateTime? dueDate, int rowVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null || (allowed.Count > 0 && !allowed.Contains(entity.ProjectId)))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        entity.Title = summary.Trim();
        entity.Description = description?.Trim();
        entity.TypeId = typeId;
        entity.PriorityId = priorityId;
        entity.StatusId = statusId;
        entity.AssignedToUserId = assignee;
        entity.DueDate = dueDate;
        entity.RowVersion = rowVersion;

        var updated = await _repository.UpdateAsync(entity, cancellationToken);
        if (!updated)
        {
            return null;
        }

        return ToResponse(entity);
    }

    public async Task<TicketResponse?> AssignAsync(long id, long? assignee, int rowVersion, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null || (allowed.Count > 0 && !allowed.Contains(entity.ProjectId)))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        entity.AssignedToUserId = assignee;
        entity.RowVersion = rowVersion;

        var updated = await _repository.UpdateAsync(entity, cancellationToken);
        if (!updated)
        {
            return null;
        }

        return ToResponse(entity);
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null || (allowed.Count > 0 && !allowed.Contains(entity.ProjectId)))
        {
            return false;
        }

        if (allowed.Count == 0)
        {
            return false;
        }

        return await _repository.DeleteByIdAsync(id, cancellationToken);
    }

    private async Task<HashSet<long>> GetAllowedProjectIdsAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var allowed = new HashSet<long>();
        if (userId is null)
        {
            return allowed;
        }

        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var membershipProjectIds = await connection.QueryAsync<long>(
            new CommandDefinition("SELECT ProjectId FROM ProjectMembers WHERE UserId = @UserId;", new { UserId = userId.Value }, cancellationToken: cancellationToken));
        foreach (var projectId in membershipProjectIds)
        {
            allowed.Add(projectId);
        }

        return allowed;
    }

    private static bool MatchesFilter(EntityTicket ticket, string property, FilterCriteria criteria)
    {
        var propertyInfo = typeof(EntityTicket).GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (propertyInfo is null)
        {
            return false;
        }

        var value = propertyInfo.GetValue(ticket);
        var op = criteria.Operator?.ToUpperInvariant() ?? "EQUALS";
        var filterValue = criteria.Value;

        if (filterValue is null)
        {
            return op is "EQUALS" or "=" && value is null;
        }

        if (op is "CONTAINS")
        {
            return value?.ToString()?.Contains(filterValue.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;
        }

        if (value is null)
        {
            return false;
        }

        if (value is IComparable comparable && filterValue is IComparable)
        {
            try
            {
                var converted = Convert.ChangeType(filterValue, value.GetType());
                var comparison = comparable.CompareTo(converted);
                return op switch
                {
                    "GT" or ">" => comparison > 0,
                    "GTE" or ">=" => comparison >= 0,
                    "LT" or "<" => comparison < 0,
                    "LTE" or "<=" => comparison <= 0,
                    _ => value.Equals(converted)
                };
            }
            catch
            {
                return value.Equals(filterValue);
            }
        }

        return value.Equals(filterValue);
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
            LoggedByUsername = string.Empty,
            NextAction = string.Empty,
            TypeId = entity.TypeId,
            StatusId = entity.StatusId,
            PriorityId = entity.PriorityId,
            DateCreated = entity.Created,
            DateResolved = entity.DueDate,
            Reference = $"TICKET-{entity.Id}",
            Assignee = entity.AssignedToUserId,
            ReportedByUsername = string.Empty,
            ProjectId = entity.ProjectId,
            ProjectCode = string.Empty
        };
        return TicketResponse.From(model);
    }
}
