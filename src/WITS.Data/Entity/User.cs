// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations.Schema;
using WITS.Data.Common;

namespace WITS.Data.Entity;

public class User : BaseEntity<long>
{
    public int OrganisationId { get; set; }

    [NotMapped]
    public Organisation Organisation { get; set; } = default!;

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    [NotMapped]
    public string FullName => $"{FirstName} {LastName}";

    public required string Username { get; set; }

    public required string EmailAddress { get; set; } = default!;
    public string? PhoneNumber { get; set; }
    public string? PictureUrl { get; set; }
    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public bool IsLocked { get; set; } = false;
}
