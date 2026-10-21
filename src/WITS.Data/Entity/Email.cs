// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class Email : BaseEntity<Guid>
{
    public Guid TicketActivityId { get; set; }
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string From { get; set; } = default!;
    public string To { get; set; } = default!;
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public DateTime SentDate { get; set; }
    public bool IsSent { get; set; } = false;
    public string? ErrorMessage { get; set; } = null!;
    public DateTime? LastAttempted { get; set; } = null;
    public int RetryCount { get; set; } = 0;
    public string? AttachmentPath { get; set; } = null!;
    public string? AttachmentName { get; set; } = null!;
    public string? AttachmentContentType { get; set; } = null!;
    public string? AttachmentContent { get; set; } = null!;


}
