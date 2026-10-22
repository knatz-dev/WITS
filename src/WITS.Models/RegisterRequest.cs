// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace WITS.Models;
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
    public int Status { get; set; }
    public int RoleLevel { get; set; }
    [Required, StringLength(1024, MinimumLength = 1)]
    public required string Password { get; set; }
    public required DateTime Created { get; set; }
    [Required, StringLength(64, MinimumLength = 1)]
    public required string TelephoneNos { get; set; }
    [Required, StringLength(128, MinimumLength = 1)]
    public required string Organisation { get; set; }
}

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
