// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using WITS.Data.Common;

namespace WITS.Data.Entity;

public class NotificationType : BaseEntity<int>
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    public required string Category { get; set; } // e.g., "Alert", "Reminder", "Info"
    public bool IsActive { get; set; } = false;

}
