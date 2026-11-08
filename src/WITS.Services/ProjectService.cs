using System.Reflection;
using Dapper;
using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using EntityProject = WITS.Data.Entity.Project;
using WITS.Models;

namespace WITS.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ProjectResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default);
    Task<ProjectResponse?> CreateAsync(string name, string? description, CancellationToken cancellationToken = default);
    Task<ProjectResponse?> UpdateAsync(long id, string name, string? description, CancellationToken cancellationToken = default);
    Task<bool> AddMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default);
    Task<bool> RetireAsync(long projectId, CancellationToken cancellationToken = default);
}

public class ProjectService : IProjectService
{
    private readonly IGenericRepository<EntityProject, long> _repository;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ProjectService(IUnitOfWork unitOfWork, IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _repository = unitOfWork.GetRepository<EntityProject, long>();
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count == 0)
        {
            return new PagedResult<ProjectResponse>([], 0, page, pageSize);
        }

        var all = await _repository.GetAllAsync(1, 100, cancellationToken);
        var filtered = all.Items.Where(project => allowed.Contains(project.Id)).Select(ToResponse).ToList();
        var total = filtered.Count;
        var paged = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<ProjectResponse>(paged, total, page, pageSize);
    }

    public async Task<ProjectResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count > 0 && !allowed.Contains(id))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count == 0)
        {
            return new PagedResult<ProjectResponse>([], 0, filter.PageNumber, filter.PageSize);
        }

        var all = await _repository.GetAllAsync(1, 100, cancellationToken);
        var candidates = all.Items.Where(project => allowed.Contains(project.Id)).ToList();

        if (filter.Filters is not null)
        {
            foreach (var (property, criteria) in filter.Filters)
            {
                candidates = candidates.Where(project => MatchesFilter(project, property, criteria)).ToList();
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.SortColumn))
        {
            var property = typeof(EntityProject).GetProperty(filter.SortColumn, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is null)
            {
                throw new ArgumentException($"Unknown sort column '{filter.SortColumn}'.", nameof(filter));
            }

            var descending = string.Equals(filter.SortOrder, "DESC", StringComparison.OrdinalIgnoreCase);
            candidates = descending
                ? candidates.OrderByDescending(project => property.GetValue(project)).ToList()
                : candidates.OrderBy(project => property.GetValue(project)).ToList();
        }

        var total = candidates.Count;
        var paged = candidates
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(ToResponse)
            .ToList();

        return new PagedResult<ProjectResponse>(paged, total, filter.PageNumber, filter.PageSize);
    }

    public async Task<ProjectResponse?> CreateAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
        {
            return null;
        }

        var userId = _currentUser.UserId;
        var organisationId = _currentUser.OrganisationId;
        if (userId is null || organisationId is null)
        {
            return null;
        }

        var project = new EntityProject
        {
            Id = 0,
            Name = name.Trim(),
            Description = description?.Trim(),
            OrganizationId = organisationId.Value,
            CreatedByUserId = userId.Value,
            Created = DateTime.UtcNow
        };

        var id = await _repository.InsertAsync(project, cancellationToken);
        project.Id = id;
        return ToResponse(project);
    }

    public async Task<ProjectResponse?> UpdateAsync(long id, string name, string? description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256)
        {
            return null;
        }

        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count > 0 && !allowed.Contains(id))
        {
            return null;
        }

        if (allowed.Count == 0)
        {
            return null;
        }

        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        entity.Name = name.Trim();
        entity.Description = description?.Trim();
        var updated = await _repository.UpdateAsync(entity, cancellationToken);
        if (!updated)
        {
            return null;
        }

        return ToResponse(entity);
    }

    public async Task<bool> AddMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT OR IGNORE INTO ProjectMembers (ProjectId, UserId, Role) VALUES (@ProjectId, @UserId, 'Member');",
                new { ProjectId = projectId, UserId = userId },
                cancellationToken: cancellationToken));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RemoveMemberAsync(long projectId, long userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM ProjectMembers WHERE ProjectId = @ProjectId AND UserId = @UserId;",
            new { ProjectId = projectId, UserId = userId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }

    public async Task<bool> RetireAsync(long projectId, CancellationToken cancellationToken = default)
    {
        var allowed = await GetAllowedProjectIdsAsync(cancellationToken);
        if (allowed.Count > 0 && !allowed.Contains(projectId))
        {
            return false;
        }

        if (allowed.Count == 0)
        {
            return false;
        }

        return await _repository.DeleteByIdAsync(projectId, cancellationToken);
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

        var projects = await _repository.GetAllAsync(1, 100, cancellationToken);
        foreach (var project in projects.Items.Where(project => project.CreatedByUserId == userId.Value))
        {
            allowed.Add(project.Id);
        }

        return allowed;
    }

    private static bool MatchesFilter(EntityProject project, string property, FilterCriteria criteria)
    {
        var propertyInfo = typeof(EntityProject).GetProperty(property, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (propertyInfo is null)
        {
            return false;
        }

        var value = propertyInfo.GetValue(project);
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

    private static ProjectResponse ToResponse(EntityProject entity) => new(
        entity.Id,
        entity.Name,
        entity.Description,
        entity.OrganizationId,
        entity.CreatedByUserId,
        entity.Created,
        entity.LastModified);
}
