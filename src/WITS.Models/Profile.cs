namespace WITS.Models;

public sealed record ProfileResponse(
    long Id,
    string Username,
    string EmailAddress,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? PictureUrl,
    int RowVersion);

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? PictureUrl,
    int RowVersion);