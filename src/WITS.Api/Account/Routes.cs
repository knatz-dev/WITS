using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Api.Auth;
using WITS.Api.Common;
using WITS.Models;
using WITS.Services;

namespace WITS.Api.Account;

[Authorize]
public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        var group = routeBuilder.MapGroup("account")
            .WithTags("Account")
            .RequireAuthorization();

        group.MapGet("profile", GetProfileHandler)
            .WithName("Get current profile")
            .WithSummary("Read the authenticated user's profile")
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("profile", UpdateProfileHandler)
            .WithName("Update current profile")
            .WithSummary("Update allowlisted personal profile fields")
            .Produces<ProfileResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<ProfileResponse>, ProblemHttpResult>> GetProfileHandler(
        IProfileService profileService, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not long userId)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Authentication is required.", ApiErrorCodes.AuthenticationRequired));
        }

        var profile = await profileService.GetAsync(userId, cancellationToken);
        if (profile is null)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Account does not exist.", ApiErrorCodes.NotFound));
        }

        return TypedResults.Ok(profile);
    }

    private static async Task<Results<Ok<ProfileResponse>, ProblemHttpResult>> UpdateProfileHandler(
        IProfileService profileService, ICurrentUser currentUser, [FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not long userId)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Authentication is required.", ApiErrorCodes.AuthenticationRequired));
        }

        if (request is null)
        {
            return TypedResults.Problem(ApiErrors.Validation("Request body is required.", ApiErrorCodes.InvalidRequest));
        }

        var result = await profileService.UpdateAsync(userId, request, cancellationToken);
        if (result.NotFound)
        {
            return TypedResults.Problem(ApiErrors.NotFound("Account does not exist.", ApiErrorCodes.NotFound));
        }
        if (result.Conflict)
        {
            return TypedResults.Problem(ApiErrors.Conflict("The profile was changed by another request.", ApiErrorCodes.Conflict));
        }
        if (!result.Success)
        {
            return TypedResults.Problem(ApiErrors.Validation(result.Error ?? "Invalid profile update.", ApiErrorCodes.ValidationFailed));
        }

        return TypedResults.Ok(result.Profile!);
    }
}