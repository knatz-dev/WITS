using Microsoft.AspNetCore.Mvc;

namespace WITS.Api.Common;

// Stable machine-readable error codes. Clients switch on these; title/detail are human-readable English and may change.
public static class ApiErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string InvalidRequest = "invalid_request";
    public const string AuthenticationRequired = "authentication_required";
    public const string InvalidCredentials = "invalid_credentials";
    public const string InvalidRefreshToken = "invalid_refresh_token";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string AccountExists = "account_exists";
    public const string RateLimited = "rate_limited";
    public const string InternalError = "internal_error";
}

// Single status-code/title mapping so routes and authentication middleware return the same problem shape.
public static class ApiErrors
{
    public static ProblemDetails Validation(string detail, string code = ApiErrorCodes.ValidationFailed) =>
        Create(StatusCodes.Status400BadRequest, "Bad Request", detail, code);

    public static ProblemDetails Unauthorized(string detail, string code = ApiErrorCodes.AuthenticationRequired) =>
        Create(StatusCodes.Status401Unauthorized, "Unauthorized", detail, code);

    public static ProblemDetails Forbidden(string detail, string code = ApiErrorCodes.Forbidden) =>
        Create(StatusCodes.Status403Forbidden, "Forbidden", detail, code);

    public static ProblemDetails NotFound(string detail, string code = ApiErrorCodes.NotFound) =>
        Create(StatusCodes.Status404NotFound, "Not Found", detail, code);

    public static ProblemDetails Conflict(string detail, string code = ApiErrorCodes.Conflict) =>
        Create(StatusCodes.Status409Conflict, "Conflict", detail, code);

    public static ProblemDetails TooManyRequests(string detail, string code = ApiErrorCodes.RateLimited) =>
        Create(StatusCodes.Status429TooManyRequests, "Too Many Requests", detail, code);

    public static ProblemDetails Internal(string detail, string code = ApiErrorCodes.InternalError) =>
        Create(StatusCodes.Status500InternalServerError, "Internal Server Error", detail, code);

    private static ProblemDetails Create(int statusCode, string title, string detail, string code)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
