// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

[Table("Tickets")]
public class Ticket: BaseEntity<long>
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public int TypeId { get; set; }
    public int PriorityId { get; set; }
    public int StatusId { get; set; }
    public long ProjectId { get; set; }
    public long CreatedByUserId { get; set; }
    public long? AssignedToUserId { get; set; }
    public DateTime? DueDate { get; set; }
}
