using WITS.Common;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Models;

namespace WITS.Services;

public record ProfileUpdateResult(bool Success, bool NotFound = false, bool Conflict = false, string? Error = null, ProfileResponse? Profile = null);

public interface IProfileService
{
    Task<ProfileResponse?> GetAsync(long userId, CancellationToken cancellationToken = default);
    Task<ProfileUpdateResult> UpdateAsync(long userId, UpdateProfileRequest request, CancellationToken cancellationToken = default);
}

public class ProfileService : IProfileService
{
    private readonly IGenericRepository<User, long> _users;

    public ProfileService(IUnitOfWork unitOfWork)
    {
        _users = unitOfWork.GetRepository<User, long>();
    }

    public async Task<ProfileResponse?> GetAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId);
        if (user is null || user.IsDeleted)
        {
            return null;
        }

        return ToResponse(user);
    }

    public async Task<ProfileUpdateResult> UpdateAsync(long userId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 128)
        {
            return new ProfileUpdateResult(false, Error: "FirstName must be 1-128 characters.");
        }
        if (string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Length > 128)
        {
            return new ProfileUpdateResult(false, Error: "LastName must be 1-128 characters.");
        }
        if (request.PhoneNumber is not null && request.PhoneNumber.Length > 64)
        {
            return new ProfileUpdateResult(false, Error: "PhoneNumber must be at most 64 characters.");
        }
        if (request.PictureUrl is not null && request.PictureUrl.Length > 512)
        {
            return new ProfileUpdateResult(false, Error: "PictureUrl must be at most 512 characters.");
        }
        if (request.RowVersion <= 0)
        {
            return new ProfileUpdateResult(false, Error: "RowVersion is required for updates.");
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null || user.IsDeleted)
        {
            return new ProfileUpdateResult(false, NotFound: true);
        }

        // Allowlisted personal fields only; identity, credentials, membership, grants, lock and audit fields stay unchanged.
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = request.PhoneNumber?.Trim();
        user.PictureUrl = request.PictureUrl?.Trim();
        user.RowVersion = request.RowVersion;

        var updated = await _users.UpdateAsync(user);
        if (!updated)
        {
            return new ProfileUpdateResult(false, Conflict: true);
        }

        return new ProfileUpdateResult(true, Profile: ToResponse(user));
    }

    private static ProfileResponse ToResponse(User user) => new(
        user.Id,
        user.Username,
        user.EmailAddress,
        user.FirstName,
        user.LastName,
        user.PhoneNumber,
        user.PictureUrl,
        user.RowVersion);
}