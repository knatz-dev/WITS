// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

[Table("Tickets")]
public class Ticket: BaseEntity<Guid>
{
    public long ProjectId { get; set; }
    public long? AssignedUserId { get; set; }
    public short TicketStatusId { get; set; }
    public short TicketPriorityId { get; set; }
    public short TicketRankId { get; set; }
    public short TicketTypeId { get; set; }

}
