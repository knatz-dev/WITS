using System.Data;
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

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IDbTransaction? _transaction;
    private readonly string _tableName;
    private readonly List<string> _columnNames;
    private readonly string _primaryKeyColumn;

    public GenericRepository(IDbConnectionFactory connectionFactory, IDbTransaction? transaction = null)
    {
        _connectionFactory = connectionFactory;
        _transaction = transaction;
        _tableName = TypeName;

        var props = typeof(T).GetProperties();
        _columnNames = props
            .Where(p => !string.Equals(p.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Name)
            .ToList();

        _primaryKeyColumn = props.FirstOrDefault(p => p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase))?.Name
                            ?? props.FirstOrDefault(p => p.Name.Equals($"{TypeName}Id", StringComparison.OrdinalIgnoreCase))?.Name
                            ?? "Id";
    }

    public async Task<T?> GetByIdAsync(TDataType id)
    {
        var query = $"SELECT * FROM {_tableName} WHERE {_primaryKeyColumn} = @Id";
        using var connection = await _connectionFactory.CreateConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<T>(query, new { Id = id }, _transaction);
    }

    public async Task<int> InsertAsync(T model)
    {
        var query = $"""
                     INSERT INTO {_tableName} ({string.Join(',', _columnNames)})
                     VALUES (@{string.Join(", @", _columnNames)});
                     SELECT last_insert_rowid();
                     """;

        using var connection = await _connectionFactory.CreateConnectionAsync();
        var id = await connection.ExecuteScalarAsync<long>(query, model, _transaction);
        return (int)id;
    }

    public async Task<bool> UpdateAsync(T model)
    {
        var setClause = string.Join(", ", _columnNames.Select(prop => $"{prop} = @{prop}"));
        var query = $"UPDATE {_tableName} SET {setClause} WHERE {_primaryKeyColumn} = @Id";

        using var connection = await _connectionFactory.CreateConnectionAsync();
        var rows = await connection.ExecuteAsync(query, model, _transaction);
        return rows > 0;
    }

    public async Task<bool> DeleteByIdAsync(TDataType id)
    {
        var query = $"DELETE FROM {_tableName} WHERE {_primaryKeyColumn} = @Id";
        using var connection = await _connectionFactory.CreateConnectionAsync();
        var rows = await connection.ExecuteAsync(query, new { Id = id }, _transaction);
        return rows > 0;
    }

    public async Task<PagedResult<T>> GetAllAsync(int pageNumber = 1, int pageSize = 100)
    {
        try
        {
            using var connection = await _connectionFactory.CreateConnectionAsync();

            var totalQuery = $"SELECT COUNT(*) FROM {_tableName} WHERE IsDeleted = 0";
            var totalRecords = await connection.ExecuteScalarAsync<int>(totalQuery, _transaction);

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

            var items = (await connection.QueryAsync<T>(query, parameters, _transaction)).ToList();

            return new PagedResult<T>(items, totalRecords, pageNumber, pageSize);
        }
        catch
        {
            return new PagedResult<T>(Array.Empty<T>(), 0, pageNumber, pageSize);
        }
    }

    public async Task<PagedResult<T>> QueryAsync(GenericFilter filter)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

        var parameters = new DynamicParameters();
        var whereConditions = new List<string> { "IsDeleted = 0" };
        int paramIndex = 0;

        if (filter.Filters?.Count > 0)
        {
            foreach (var (property, criteria) in filter.Filters)
            {
                if (!_columnNames.Contains(property, StringComparer.OrdinalIgnoreCase) &&
                    !property.Equals(_primaryKeyColumn, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var op = criteria.Operator?.ToUpperInvariant() ?? "EQUALS";
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
        var totalRecords = await connection.ExecuteScalarAsync<int>(countQuery, parameters, _transaction);

        var sortColumn = !string.IsNullOrWhiteSpace(filter.SortColumn) && _columnNames.Contains(filter.SortColumn)
            ? filter.SortColumn
            : _primaryKeyColumn;

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

        var items = (await connection.QueryAsync<T>(query, parameters, _transaction)).ToList();

        return new PagedResult<T>(items, totalRecords, filter.PageNumber, filter.PageSize);
    }

    public async Task<PagedResult<T>> SearchAsync(SearchParams searchParams)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync();

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

        var totalRecords = await connection.ExecuteScalarAsync<int>(countQuery, parameters, _transaction);

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

        var items = (await connection.QueryAsync<T>(searchQuery, parameters, _transaction)).ToList();

        return new PagedResult<T>(items, totalRecords, searchParams.PageNumber, searchParams.PageSize);
    }
}
