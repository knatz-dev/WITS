// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class ProjectUserRole: BaseEntity<Guid>
{
    public long UserId { get; set; }
    public short RoleId { get; set; }
    public long ProjectId { get; set; }
}
