using WITS.Common;
using WITS.Data.Common;

namespace WITS.Data.Contracts;

public interface IGenericRepository<T, TDataType> where T : BaseEntity<TDataType>
{
    Task<PagedResult<T>> GetAllAsync(int pageNumber = 1, int pageSize = 100, CancellationToken cancellationToken = default);
    Task<PagedResult<T>> QueryAsync(GenericFilter filter, CancellationToken cancellationToken = default);
    Task<T?> GetByIdAsync(TDataType id, CancellationToken cancellationToken = default);
    Task<TDataType> InsertAsync(T model, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(T model, CancellationToken cancellationToken = default);
    Task<bool> DeleteByIdAsync(TDataType id, CancellationToken cancellationToken = default);
    Task<PagedResult<T>> SearchAsync(SearchParams searchParams, CancellationToken cancellationToken = default);
}
