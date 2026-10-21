using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WITS.Services;
using WITS.Api.Common;
using WITS.Models;

namespace WITS.Api.Auth;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string? Token, string? Message);

public class Routes : IEndpointDefinition
{
    public void Register(RouteGroupBuilder routeBuilder)
    {
        RouteGroupBuilder authGroup = routeBuilder.MapGroup("auth")
            .WithTags("Authentication");
        authGroup.MapPost("login", LoginHandler)
            .WithName("Login")
            .AllowAnonymous();
        authGroup.MapPost("register", RegisterHandler)
            .WithName("Register")
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<LoginResponse>, BadRequest<string>>> LoginHandler(
        IAuthService authService, [FromBody] LoginRequest request)
    {
        var credentialResult = await authService.ValidateCredentialsAsync(request.Username, request.Password);
        if (!credentialResult.Success)
        {
            return TypedResults.BadRequest("Invalid credentials");
        }
        return TypedResults.Ok(new LoginResponse(credentialResult.Token, credentialResult.Message));
    }

    private static async Task<Results<Ok<string>, BadRequest<string>>> RegisterHandler(
        IAuthService authService, [FromBody] RegisterRequest request)
    {
        var result = await authService.RegisterUserAsync(request);
        if (!result.Success)
        {
            return TypedResults.BadRequest(result.Message);
        }
        return TypedResults.Ok(result.Message);
    }
}
