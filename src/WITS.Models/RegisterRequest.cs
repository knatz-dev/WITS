// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace WITS.Models;

// Accepted registration input. Identifiers, timestamps and status are server-owned and
// deliberately absent so callers cannot choose their own persistence values.
public class RegisterRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Username { get; set; }
    [Required, StringLength(128, MinimumLength = 1)]
    public required string FirstName { get; set; }
    [Required, StringLength(128, MinimumLength = 1)]
    public required string LastName { get; set; }
    [Required, EmailAddress, StringLength(256)]
    public required string EmailAddress { get; set; }
    [Range(0, int.MaxValue)]
    public int OrganisationId { get; set; }
    [Required, StringLength(1024, MinimumLength = 1)]
    public required string Password { get; set; }
    [Required, StringLength(64, MinimumLength = 1)]
    public required string TelephoneNos { get; set; }
}

// Registration response: only the server-owned identity and the accepted fields.
public record RegisterResponse(long Id, string Username, string EmailAddress);

public class UserTokenModel
{
    public required long Id { get; set; }
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Username { get; set; }
    [Required, EmailAddress, StringLength(256)]
    public required string EmailAddress { get; set; }
    [Range(0, int.MaxValue)]
    public int OrganisationId { get; set; }
}
