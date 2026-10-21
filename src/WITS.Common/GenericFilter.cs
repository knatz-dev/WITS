namespace WITS.Common;

public record GenericFilter(
    Dictionary<string, FilterCriteria>? Filters = null,
    string? SortColumn = null,
    string SortOrder = "ASC",
    int PageNumber = 1,
    int PageSize = 10
);
