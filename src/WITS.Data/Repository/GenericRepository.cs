using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using System.Text.Json;
using Dapper;
using WITS.Common;
using WITS.Data.Common;
using WITS.Data.Contracts;

namespace WITS.Data.Repository;

public class GenericRepository<T, TDataType> : IGenericRepository<T, TDataType>
    where T : BaseEntity<TDataType>
{
    private static readonly string TypeName = typeof(T).Name;
    public const int MaxPageSize = 100;
    private const int MaxFilterCount = 10;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly Func<IDbTransaction?>? _currentTransaction;
    private readonly bool _ownsConnection;
    private readonly string _tableName;
    private readonly List<string> _columnNames;
    private readonly string _primaryKeyColumn;

    public GenericRepository(IDbConnectionFactory connectionFactory, Func<IDbTransaction?>? currentTransaction = null, bool ownsConnection = true)
    {
        _connectionFactory = connectionFactory;
        _currentTransaction = currentTransaction;
        // The unit of work borrows its shared connection; standalone factories hand out
        // per-call connections that this instance must release after each operation.
        _ownsConnection = ownsConnection;
        // Explicit [Table] mapping wins; otherwise the class name is the table name.
        _tableName = typeof(T).GetCustomAttribute<TableAttribute>(inherit: true)?.Name ?? TypeName;

        var props = typeof(T).GetProperties();
        _columnNames = props
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase) && IsStoredColumn(p))
            .Select(p => p.Name)
            .ToList();

        _primaryKeyColumn = props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))?.Name
                            ?? props.FirstOrDefault(p => p.Name.Equals($"{TypeName}Id", StringComparison.OrdinalIgnoreCase))?.Name
                            ?? "Id";
    }

    // Persisted columns are writable properties with a SQLite-storable scalar type; this keeps
    // navigation, collection and computed properties out of generated INSERT/UPDATE statements.
    private static bool IsStoredColumn(PropertyInfo property)
    {
        if (!property.CanWrite || Attribute.IsDefined(property, typeof(NotMappedAttribute), inherit: true))
        {
            return false;
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(TimeSpan)
               || type == typeof(Guid)
               || type == typeof(byte[]);
    }

    // Live view of the owning transaction: repositories cached before BeginTransaction must still enlist.
    private IDbTransaction? Transaction => _currentTransaction?.Invoke();

    public async Task<T?> GetByIdAsync(TDataType id)
    {
        var query = $"SELECT * FROM {_tableName} WHERE {_primaryKeyColumn} = @Id AND IsDeleted = 0";
        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;
        return await connection.QuerySingleOrDefaultAsync<T>(query, new { Id = id }, Transaction);
    }

    public async Task<int> InsertAsync(T model)
    {
        // Every writer shares one UTC default for Created, while an explicitly
        // supplied value (tests, seeder) is kept as-is.
        if (model.Created == default)
        {
            model.Created = DateTime.UtcNow;
        }

        var query = $"""
                     INSERT INTO {_tableName} ({string.Join(',', _columnNames)})
                     VALUES (@{string.Join(", @", _columnNames)});
                     SELECT last_insert_rowid();
                     """;

        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;
        var id = await connection.ExecuteScalarAsync<long>(query, model, Transaction);
        return (int)id;
    }

    public async Task<bool> UpdateAsync(T model)
    {
        model.LastModified = DateTime.UtcNow;
        var setClause = string.Join(", ", _columnNames.Select(prop => $"{prop} = @{prop}"));
        var query = $"UPDATE {_tableName} SET {setClause} WHERE {_primaryKeyColumn} = @Id";

        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;
        var rows = await connection.ExecuteAsync(query, model, Transaction);
        return rows > 0;
    }

    // shortcut: soft delete keeps the row, so reads hide it through the IsDeleted flag instead of
    // erasing it, and the table must carry IsDeleted and LastModified columns — tables without them
    // already fail the read filters. Upgrade to a hard purge only when retention demands it.
    public async Task<bool> DeleteByIdAsync(TDataType id)
    {
        var query = $"UPDATE {_tableName} SET IsDeleted = 1, LastModified = @LastModified WHERE {_primaryKeyColumn} = @Id AND IsDeleted = 0";
        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;
        var rows = await connection.ExecuteAsync(query, new { Id = id, LastModified = DateTime.UtcNow }, Transaction);
        return rows > 0;
    }

    public async Task<PagedResult<T>> GetAllAsync(int pageNumber = 1, int pageSize = 100)
    {
        if (pageNumber < 1) throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be at least 1.");
        if (pageSize < 1 || pageSize > MaxPageSize) throw new ArgumentOutOfRangeException(nameof(pageSize), $"Page size must be between 1 and {MaxPageSize}.");
        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;

        var totalQuery = $"SELECT COUNT(*) FROM {_tableName} WHERE IsDeleted = 0";
        var totalRecords = await connection.ExecuteScalarAsync<int>(totalQuery, Transaction);

        var offset = (pageNumber - 1) * pageSize;

        var query = $"""
                     SELECT * FROM {_tableName}
                     WHERE IsDeleted = 0
                     ORDER BY {_primaryKeyColumn}
                     LIMIT @PageSize OFFSET @Offset
                     """;

        var parameters = new DynamicParameters();
        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", pageSize);

        var items = (await connection.QueryAsync<T>(query, parameters, Transaction)).ToList();

        return new PagedResult<T>(items, totalRecords, pageNumber, pageSize);
    }

    public async Task<PagedResult<T>> QueryAsync(GenericFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.PageNumber < 1) throw new ArgumentOutOfRangeException(nameof(filter), "Page number must be at least 1.");
        if (filter.PageSize < 1 || filter.PageSize > MaxPageSize) throw new ArgumentOutOfRangeException(nameof(filter), $"Page size must be between 1 and {MaxPageSize}.");
        if (filter.SortOrder is not null && filter.SortOrder is not ("" or "ASC" or "DESC" or "asc" or "desc"))
            throw new ArgumentException("Sort order must be ASC or DESC.", nameof(filter));
        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;

        var parameters = new DynamicParameters();
        var whereConditions = new List<string> { "IsDeleted = 0" };
        int paramIndex = 0;

        if (filter.Filters is { Count: > MaxFilterCount })
        {
            throw new ArgumentException($"A query may specify at most {MaxFilterCount} filters.", nameof(filter));
        }
        if (filter.Filters?.Count > 0)
        {
            foreach (var (property, criteria) in filter.Filters)
            {
                if (criteria is null) throw new ArgumentException("Filter criteria cannot be null.", nameof(filter));
                if (!_columnNames.Contains(property, StringComparer.OrdinalIgnoreCase) &&
                    !property.Equals(_primaryKeyColumn, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"Unknown filter field '{property}'.", nameof(filter));
                }

                var op = criteria.Operator?.ToUpperInvariant() ?? "EQUALS";
                var knownOps = new[] { "CONTAINS", "GT", ">", "GTE", ">=", "LT", "<", "LTE", "<=", "EQUALS", "=" };
                if (!knownOps.Contains(op)) throw new ArgumentException($"Unknown filter operator '{criteria.Operator}'.", nameof(filter));

                var paramName = $"@p{paramIndex++}";
                var value = criteria.Value is JsonElement json ? json.ToPrimitive() : criteria.Value;

                switch (op)
                {
                    case "CONTAINS":
                        whereConditions.Add($"{property} LIKE {paramName}");
                        parameters.Add(paramName, $"%{value}%");
                        break;
                    case "GT":
                    case ">":
                        whereConditions.Add($"{property} > {paramName}");
                        parameters.Add(paramName, value);
                        break;
                    case "GTE":
                    case ">=":
                        whereConditions.Add($"{property} >= {paramName}");
                        parameters.Add(paramName, value);
                        break;
                    case "LT":
                    case "<":
                        whereConditions.Add($"{property} < {paramName}");
                        parameters.Add(paramName, value);
                        break;
                    case "LTE":
                    case "<=":
                        whereConditions.Add($"{property} <= {paramName}");
                        parameters.Add(paramName, value);
                        break;
                    case "EQUALS":
                    case "=":
                        whereConditions.Add($"{property} = {paramName}");
                        parameters.Add(paramName, value);
                        break;
                }
            }
        }

        var whereClause = whereConditions.Count > 0 ? $"WHERE {string.Join(" AND ", whereConditions)}" : "";

        var countQuery = $"SELECT COUNT(*) FROM {_tableName} {whereClause}";
        var totalRecords = await connection.ExecuteScalarAsync<int>(countQuery, parameters, Transaction);

        string sortColumn;
        if (string.IsNullOrWhiteSpace(filter.SortColumn))
        {
            sortColumn = _primaryKeyColumn;
        }
        else if (!_columnNames.Contains(filter.SortColumn, StringComparer.OrdinalIgnoreCase)
            && !filter.SortColumn.Equals(_primaryKeyColumn, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unknown sort column '{filter.SortColumn}'.", nameof(filter));
        }
        else
        {
            sortColumn = filter.SortColumn;
        }

        var sortOrder = filter.SortOrder?.ToUpperInvariant() == "DESC" ? "DESC" : "ASC";
        var offset = (filter.PageNumber - 1) * filter.PageSize;

        var query = $"""
                     SELECT * FROM {_tableName}
                     {whereClause}
                     ORDER BY {sortColumn} {sortOrder}
                     LIMIT @PageSize OFFSET @Offset
                     """;

        parameters.Add("@Offset", offset);
        parameters.Add("@PageSize", filter.PageSize);

        var items = (await connection.QueryAsync<T>(query, parameters, Transaction)).ToList();

        return new PagedResult<T>(items, totalRecords, filter.PageNumber, filter.PageSize);
    }

    public async Task<PagedResult<T>> SearchAsync(SearchParams searchParams)
    {
        var connection = await _connectionFactory.CreateConnectionAsync();
        using var owned = _ownsConnection ? connection : null;

        var ftsTable = $"{_tableName}FTS";

        var parameters = new DynamicParameters();
        parameters.Add("@SearchTerm", searchParams.SearchString);
        parameters.Add("@OrganisationId", searchParams.OrganisationId);
        parameters.Add("@Offset", (searchParams.PageNumber - 1) * searchParams.PageSize);
        parameters.Add("@PageSize", searchParams.PageSize);

        var countQuery = $"""
                          SELECT COUNT(*)
                          FROM {_tableName} AS main
                          JOIN {ftsTable} AS fts ON fts.rowid = main.Id
                          WHERE fts MATCH @SearchTerm
                          AND main.IsDeleted = 0
                          AND main.OrganisationId = @OrganisationId
                          """;

        var totalRecords = await connection.ExecuteScalarAsync<int>(countQuery, parameters, Transaction);

        var searchQuery = $"""
                           SELECT main.*, bm25(fts) AS Relevance
                           FROM {_tableName} AS main
                           JOIN {ftsTable} AS fts ON fts.rowid = main.Id
                           WHERE fts MATCH @SearchTerm
                           AND main.IsDeleted = 0
                           AND main.OrganisationId = @OrganisationId
                           ORDER BY Relevance ASC, main.Id DESC
                           LIMIT @PageSize OFFSET @Offset
                           """;

        var items = (await connection.QueryAsync<T>(searchQuery, parameters, Transaction)).ToList();

        return new PagedResult<T>(items, totalRecords, searchParams.PageNumber, searchParams.PageSize);
    }
}
