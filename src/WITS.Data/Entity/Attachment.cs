// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using WITS.Data.Common;

namespace WITS.Data.Entity;

public class Attachment : BaseEntity<Guid>
{
    public Guid TicketActivityId { get; set; }
    public string FilePath { get; set; } = default!;

    public string FileName { get; set; } = default!;

    public string FileType { get; set; } = default!;

    public long FileSize { get; set; } = 0L;

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string UploadedBy { get; set; } = default!;

    public string? Description { get; set; } = null;

}
