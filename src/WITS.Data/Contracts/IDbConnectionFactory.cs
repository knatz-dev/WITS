using System.Data;

namespace WITS.Data.Contracts;

public interface IDbConnectionFactory
{
    IDbConnection CreateConnection();
    Task<IDbConnection> CreateConnectionAsync(CancellationToken token = default);
}
