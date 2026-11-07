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

    public ProjectService(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.GetRepository<EntityProject, long>();
    }

    public async Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var result = await _repository.GetAllAsync(page, pageSize, cancellationToken);
        return new PagedResult<ProjectResponse>(result.Items.Select(ToResponse).ToList(), result.TotalRecords, result.PageNumber, result.PageSize);
    }

    public async Task<ProjectResponse?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default)
    {
        var result = await _repository.QueryAsync(filter, cancellationToken);
        return new PagedResult<ProjectResponse>(result.Items.Select(ToResponse).ToList(), result.TotalRecords, result.PageNumber, result.PageSize);
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
