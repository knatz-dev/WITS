namespace WITS.Common;

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalRecords,
    int PageNumber,
    int PageSize
)
{
    public int TotalPages => (int)Math.Ceiling(TotalRecords / (double)PageSize);
}
