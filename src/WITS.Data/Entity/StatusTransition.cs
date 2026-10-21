// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class StatusTransition : BaseEntity<Guid>
{
    public long WorkFlowId { get; set; }
    public long FromStatusId { get; set; }
    public long ToStatusId { get; set; }
    public long? RequiredRoleId { get; set; }

}
