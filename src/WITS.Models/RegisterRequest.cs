// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace WITS.Models;
public class RegisterRequest
{
    public required string Username { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string EmailAddress { get; set; }
    public int OrganisationId { get; set; }
    public int Status { get; set; }
    public int RoleLevel { get; set; }
    public required string Password { get; set; }
    public required DateTime Created { get; set; }
    public required string TelephoneNos { get; set; }
    public required string Organisation { get; set; }
}

public class UserTokenModel
{
    public required long Id { get; set; }
    public required string Username { get; set; }
    public required string EmailAddress { get; set; }
    public int OrganisationId { get; set; }
}
