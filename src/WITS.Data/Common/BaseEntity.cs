namespace WITS.Data.Common;

public abstract class BaseEntity<TDataType>
{
    public required TDataType Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastModified { get; set; }
    // Optimistic concurrency token. Incremented on each update; if a concurrent write
    // advanced the version, the update affects zero rows and reports a conflict.
    public int RowVersion { get; set; }
}
