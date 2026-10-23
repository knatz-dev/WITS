using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Services;
using WITS.Api.Common;
using WITS.Models;
using WITS.Common;

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
            .WithSummary("Authenticate with username and password")
            .WithDescription("Returns a short-lived access token and a rotating refresh token. "
                             + "Passwords are never echoed. Rejected credentials return 401 with a generic detail.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
        authGroup.MapPost("register", RegisterHandler)
            .WithName("Register")
            .WithSummary("Register a new account")
            .WithDescription("Accepts client-owned fields only; id, timestamps, status and password hash are server-owned. "
                             + "Returns the new identity without credentials. Duplicate usernames return 409.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
        authGroup.MapPost("refresh", RefreshHandler)
            .WithName("Refresh")
            .WithSummary("Rotate a refresh token")
            .WithDescription("Each successful refresh returns a new access/refresh pair and invalidates the presented token. "
                             + "Expired, revoked, unknown or reused tokens return 401.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
        authGroup.MapPost("logout", LogoutHandler)
            .WithName("Logout")
            .WithSummary("Revoke a refresh token")
            .WithDescription("Revokes the supplied refresh token. Already-issued access tokens remain valid until they expire.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> LoginHandler(
        IAuthService authService, [FromBody] LoginRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password)
            || request.Username.Length > 128 || request.Password.Length > 1024)
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "invalid_input"));
            return TypedResults.Problem(ApiErrors.Validation("Username and password are required.", ApiErrorCodes.ValidationFailed));
        }
        var credentialResult = await authService.ValidateCredentialsAsync(request.Username, request.Password);
        if (!credentialResult.Success)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized("Invalid credentials", ApiErrorCodes.InvalidCredentials));
        }
        return TypedResults.Ok(new LoginResponse(credentialResult.Token, credentialResult.RefreshToken, credentialResult.Message));
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> RefreshHandler(
        IAuthService authService, [FromBody] RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Problem(ApiErrors.Unauthorized(
                "Invalid refresh token", ApiErrorCodes.InvalidRefreshToken));
        }
        var result = await authService.RefreshAsync(request.RefreshToken);
        if (!result.Success)
        {
            return TypedResults.Problem(ApiErrors.Unauthorized(result.Message, ApiErrorCodes.InvalidRefreshToken));
        }
        return TypedResults.Ok(new LoginResponse(result.AccessToken, result.RefreshToken, result.Message));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LogoutHandler(
        IAuthService authService, [FromBody] LogoutRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.Problem(ApiErrors.Unauthorized(
                "Invalid refresh token", ApiErrorCodes.InvalidRefreshToken));
        }
        authService.RevokeRefreshToken(request.RefreshToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<RegisterResponse>, ProblemHttpResult>> RegisterHandler(
        IAuthService authService, [FromBody] RegisterRequest request)
    {
        if (request is null)
        {
            return TypedResults.Problem(ApiErrors.Validation("Request body is required.", ApiErrorCodes.InvalidRequest));
        }
        request.Username = request.Username?.Trim() ?? string.Empty;
        request.EmailAddress = request.EmailAddress?.Trim() ?? string.Empty;
        request.FirstName = request.FirstName?.Trim() ?? string.Empty;
        request.LastName = request.LastName?.Trim() ?? string.Empty;
        request.Password = request.Password ?? string.Empty;
        request.TelephoneNos = request.TelephoneNos?.Trim() ?? string.Empty;
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(request);
        if (!System.ComponentModel.DataAnnotations.Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true))
        {
            var detail = string.Join(" ", validationResults.Select(r => r.ErrorMessage).Where(m => !string.IsNullOrWhiteSpace(m)));
            return TypedResults.Problem(ApiErrors.Validation(string.IsNullOrWhiteSpace(detail) ? "Invalid registration request." : detail, ApiErrorCodes.ValidationFailed));
        }
        var result = await authService.RegisterUserAsync(request);
        if (!result.Success)
        {
            return result.Conflict
                ? TypedResults.Problem(ApiErrors.Conflict(result.Message, ApiErrorCodes.AccountExists))
                : TypedResults.Problem(ApiErrors.Validation(result.Message, ApiErrorCodes.ValidationFailed));
        }
        return TypedResults.Ok(new RegisterResponse(result.UserId ?? 0, request.Username, request.EmailAddress));
    }
}