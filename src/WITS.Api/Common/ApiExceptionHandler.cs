using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using WITS.Common;

namespace WITS.Api.Common;

// One mapping for unhandled failures: unreadable requests stay 400, everything else becomes an opaque 500 carrying only a trace id.
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is SqliteException { SqliteErrorCode: 5 or 6 } sqlite)
        {
            OperationalSignals.SqliteContention.Add(1, new KeyValuePair<string, object?>(
                "error.type", sqlite.SqliteErrorCode == 5 ? "busy" : "locked"));
        }

        var invalidRequest = exception is BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest }
            or ArgumentException;
        var problem = invalidRequest
            ? ApiErrors.Validation(exception.Message)
            : ApiErrors.Internal("An error occurred while processing your request.");
        // The client-visible trace id: the problem-details writer overwrites traceId with this same expression.
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = problem.Status!.Value;

        if (!invalidRequest)
        {
            logger.LogError(exception, "Unhandled exception handling {Method} {Path} with trace id {TraceId}",
                httpContext.Request.Method, httpContext.Request.Path, traceId);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return true;
    }
}
