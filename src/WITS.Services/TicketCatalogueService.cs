using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Models;

namespace WITS.Services;

public interface ITicketCatalogueService
{
    Task<IReadOnlyList<CatalogueItemResponse>> GetTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogueItemResponse>> GetPrioritiesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CatalogueItemResponse>> GetStatusesAsync(CancellationToken cancellationToken = default);
}

public class TicketCatalogueService : ITicketCatalogueService
{
    private readonly IGenericRepository<TicketType, int> _types;
    private readonly IGenericRepository<TicketPriority, int> _priorities;
    private readonly IGenericRepository<TicketStatus, int> _statuses;

    public TicketCatalogueService(IUnitOfWork unitOfWork)
    {
        _types = unitOfWork.GetRepository<TicketType, int>();
        _priorities = unitOfWork.GetRepository<TicketPriority, int>();
        _statuses = unitOfWork.GetRepository<TicketStatus, int>();
    }

    public async Task<IReadOnlyList<CatalogueItemResponse>> GetTypesAsync(CancellationToken cancellationToken = default)
    {
        var page = await _types.GetAllAsync(1, 100, cancellationToken);
        return page.Items.Select(item => new CatalogueItemResponse(item.Id, item.Name)).ToList();
    }

    public async Task<IReadOnlyList<CatalogueItemResponse>> GetPrioritiesAsync(CancellationToken cancellationToken = default)
    {
        var page = await _priorities.GetAllAsync(1, 100, cancellationToken);
        return page.Items.Select(item => new CatalogueItemResponse(item.Id, item.Name)).ToList();
    }

    public async Task<IReadOnlyList<CatalogueItemResponse>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var page = await _statuses.GetAllAsync(1, 100, cancellationToken);
        return page.Items.Select(item => new CatalogueItemResponse(item.Id, item.Name)).ToList();
    }
}