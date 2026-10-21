using WITS.Common;
using WITS.Models;

namespace WITS.Services;

public interface ITicketService
{
    Task<PagedResult<Ticket>> GetAllAsync(int page, int pageSize);
    Task<Ticket> GetByIdAsync(long id);
    Task<PagedResult<Ticket>> QueryAsync(GenericFilter filter);
}

public class TicketService : ITicketService
{
    public Task<PagedResult<Ticket>> GetAllAsync(int page, int pageSize) => throw new NotImplementedException();
    public Task<Ticket> GetByIdAsync(long id) => throw new NotImplementedException();
    public Task<PagedResult<Ticket>> QueryAsync(GenericFilter filter) => throw new NotImplementedException();
}
