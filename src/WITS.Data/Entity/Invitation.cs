using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

[Table("Invitations")]
public class Invitation : BaseEntity<int>
{
    public int OrganisationId { get; set; }
    public required string Code { get; set; }
    public long CreatedByUserId { get; set; }
    public long? UsedByUserId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public bool IsRevoked { get; set; }
}