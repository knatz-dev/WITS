using System.Data;
using WITS.Data.Common;
using WITS.Data.Contracts;
using WITS.Data.Factory;
using WITS.Data.Repository;

namespace WITS.Data;

public interface IUnitOfWork : IDisposable
{
    IGenericRepository<T, TId> GetRepository<T, TId>() where T : BaseEntity<TId>;
    void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Serializable);
    void Commit();
    void Rollback();
}

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly IDbConnection _connection;
    private IDbTransaction? _transaction;
    private readonly Dictionary<Type, object> _repositories = new();

    public UnitOfWork(IDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connection = connectionFactory.CreateConnectionAsync().Result;

        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }
    }

    public void BeginTransaction(IsolationLevel isolationLevel = IsolationLevel.Serializable)
    {
        if (_connection == null)
        {
            throw new InvalidOperationException("Database connection is not initialized.");
        }

        if (_connection.State != ConnectionState.Open)
        {
            _connection.Open();
        }

        _transaction = _connection.BeginTransaction(isolationLevel);
    }

    public void Commit()
    {
        _transaction?.Commit();
        DisposeTransaction();
    }

    public void Rollback()
    {
        _transaction?.Rollback();
        DisposeTransaction();
    }

    public IGenericRepository<T, TId> GetRepository<T, TId>() where T : BaseEntity<TId>
    {
        var type = typeof(T);
        if (!_repositories.ContainsKey(type))
        {
            // The repository borrows this unit of work's shared connection and reads the
            // live transaction instead of a construction-time snapshot, so it joins
            // transactions started after the repository was cached.
            var instance = new GenericRepository<T, TId>(
                new StaticConnectionFactory(_connection),
                () => _transaction,
                ownsConnection: false
            );
            _repositories[type] = instance;
        }

        return (IGenericRepository<T, TId>)_repositories[type]!;
    }

    private void DisposeTransaction()
    {
        _transaction?.Dispose();
        _transaction = null;
    }

    public void Dispose()
    {
        DisposeTransaction();

        if (_connection?.State == ConnectionState.Open)
        {
            _connection.Close();
        }

        _connection?.Dispose();
    }
}
