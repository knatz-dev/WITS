using WITS.Common;
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
    public Task<PagedResult<TicketResponse>> GetAllAsync(int page, int pageSize) => throw new NotImplementedException();
    public Task<TicketResponse?> GetByIdAsync(long id) => throw new NotImplementedException();
    public Task<PagedResult<TicketResponse>> QueryAsync(GenericFilter filter) => throw new NotImplementedException();
}
