// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

[Table("UserRoles")]
public class UserRole : BaseEntity<Guid>
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public Guid OrganisationId { get; set; }
}
