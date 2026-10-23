// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

[Table("ProjectMembers")]
public class ProjectMember: BaseEntity<Guid>
{
    public long ProjectId { get; set; }
    public long UserId { get; set; }
}
