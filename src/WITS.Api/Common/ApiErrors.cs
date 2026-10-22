using Microsoft.AspNetCore.Mvc;

namespace WITS.Api.Common;

// Single status-code/title mapping so routes and authentication middleware return the same problem shape.
public static class ApiErrors
{
    public static ProblemDetails Validation(string detail) =>
        Create(StatusCodes.Status400BadRequest, "Bad Request", detail);

    public static ProblemDetails Unauthorized(string detail) =>
        Create(StatusCodes.Status401Unauthorized, "Unauthorized", detail);

    public static ProblemDetails Forbidden(string detail) =>
        Create(StatusCodes.Status403Forbidden, "Forbidden", detail);

    public static ProblemDetails NotFound(string detail) =>
        Create(StatusCodes.Status404NotFound, "Not Found", detail);

    public static ProblemDetails Conflict(string detail) =>
        Create(StatusCodes.Status409Conflict, "Conflict", detail);

    public static ProblemDetails TooManyRequests(string detail) =>
        Create(StatusCodes.Status429TooManyRequests, "Too Many Requests", detail);

    public static ProblemDetails Internal(string detail) =>
        Create(StatusCodes.Status500InternalServerError, "Internal Server Error", detail);

    private static ProblemDetails Create(int statusCode, string title, string detail) => new()
    {
        Status = statusCode,
        Title = title,
        Detail = detail
    };
}
