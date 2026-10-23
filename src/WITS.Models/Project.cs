namespace WITS.Models;

public class Project
{
    public long Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastModified { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int OrganizationId { get; set; }
    public long CreatedByUserId { get; set; }
}

public sealed record ProjectResponse(
    long Id,
    string Name,
    string? Description,
    int OrganizationId,
    long CreatedByUserId,
    DateTime Created,
    DateTime? LastModified)
{
    public static ProjectResponse From(Project project) => new(
        project.Id,
        project.Name,
        project.Description,
        project.OrganizationId,
        project.CreatedByUserId,
        project.Created,
        project.LastModified);
}

// Keep an entity-to-model bridge not needed; services map manually.
