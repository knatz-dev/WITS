// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class Project: BaseEntity<long>
{
    public Guid OrganisationId { get; set; }
    public required string Name { get; set; }

    public Guid? WorkFlowId { get; set; }
    
}
