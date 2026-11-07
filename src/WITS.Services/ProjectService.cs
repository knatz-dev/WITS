using System.Reflection;
using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using EntityProject = WITS.Data.Entity.Project;
using WITS.Models;

namespace WITS.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<ProjectResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default);
}

public class ProjectService : IProjectService
{
    private readonly IGenericRepository<EntityProject, long> _repository;
    private readonly IGenericRepository<WITS.Data.Entity.ProjectMember, Guid> _members;
    private readonly ICurrentUser _currentUser;

    public ProjectService(IUnitOfWork unitOfWork, ICurrentUser currentUser)
    {
        _repository = unitOfWork.GetRepository<EntityProject, long>();
        _members = unitOfWork.GetRepository<WITS.Data.Entity.ProjectMember, Guid>();
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

    private async Task<HashSet<long>> GetAllowedProjectIdsAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var allowed = new HashSet<long>();
        if (userId is null)
        {
            return allowed;
        }

        var memberships = await _members.GetAllAsync(1, 100, cancellationToken);
        foreach (var membership in memberships.Items.Where(member => member.UserId == userId.Value))
        {
            allowed.Add(membership.ProjectId);
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
