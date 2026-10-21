// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class Notification: BaseEntity<Guid>
{
    public Guid UserId { get; set; }
 
    public Guid? TicketId { get; set; }
    public Guid? TicketActivityId { get; set; }

    public int NotificationTypeId { get; set; }

    public required string Message { get; set; }

}
