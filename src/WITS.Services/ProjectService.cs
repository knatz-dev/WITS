using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using EntityProject = WITS.Data.Entity.Project;
using WITS.Models;

namespace WITS.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize);
    Task<ProjectResponse?> GetByIdAsync(long id);
    Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter);
}

public class ProjectService : IProjectService
{
    private readonly IGenericRepository<EntityProject, long> _repository;

    public ProjectService(IUnitOfWork unitOfWork)
    {
        _repository = unitOfWork.GetRepository<EntityProject, long>();
    }

    public async Task<PagedResult<ProjectResponse>> GetAllAsync(int page, int pageSize)
    {
        var result = await _repository.GetAllAsync(page, pageSize);
        return new PagedResult<ProjectResponse>(result.Items.Select(ToResponse).ToList(), result.TotalRecords, result.PageNumber, result.PageSize);
    }

    public async Task<ProjectResponse?> GetByIdAsync(long id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : ToResponse(entity);
    }

    public async Task<PagedResult<ProjectResponse>> QueryAsync(GenericFilter filter)
    {
        var result = await _repository.QueryAsync(filter);
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
