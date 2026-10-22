using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Services;
using WITS.Api.Common;
using WITS.Models;

namespace WITS.Api.Auth;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string? Token, string? RefreshToken, string? Message);
public record RefreshRequest(string RefreshToken);
public record LogoutRequest(string RefreshToken);

public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        RouteGroupBuilder authGroup = routeBuilder.MapGroup("auth")
            .WithTags("Authentication")
            .RequireRateLimiting(AuthenticationRateLimits.Policy);
        authGroup.MapPost("login", LoginHandler)
            .WithName("Login")
            .AllowAnonymous();
        authGroup.MapPost("register", RegisterHandler)
            .WithName("Register")
            .AllowAnonymous();
        authGroup.MapPost("refresh", RefreshHandler)
            .WithName("Refresh")
            .AllowAnonymous();
        authGroup.MapPost("logout", LogoutHandler)
            .WithName("Logout")
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginHandler(
        IAuthService authService, [FromBody] LoginRequest request)
    {
        var credentialResult = await authService.ValidateCredentialsAsync(request.Username, request.Password);
        if (!credentialResult.Success)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Invalid credentials"));
        }
        return TypedResults.Ok(new LoginResponse(credentialResult.Token, credentialResult.RefreshToken, credentialResult.Message));
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> RefreshHandler(
        IAuthService authService, [FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Invalid refresh token"));
        }
        var result = await authService.RefreshAsync(request.RefreshToken);
        if (!result.Success)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized(result.Message));
        }
        return TypedResults.Ok(new LoginResponse(result.AccessToken, result.RefreshToken, result.Message));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LogoutHandler(
        IAuthService authService, [FromBody] LogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Invalid refresh token"));
        }
        authService.RevokeRefreshToken(request.RefreshToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<string>, ProblemHttpResult>> RegisterHandler(
        IAuthService authService, [FromBody] RegisterRequest request)
    {
        var result = await authService.RegisterUserAsync(request);
        if (!result.Success)
        {
            return result.Conflict
                ? TypedResults.Problem(ApiErrors.Conflict(result.Message))
                : TypedResults.Problem(ApiErrors.Validation(result.Message));
        }
        return TypedResults.Ok(result.Message);
    }
}