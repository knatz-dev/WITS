using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WITS.Api.Common;

// One mapping for unhandled failures: unreadable requests stay 400, everything else becomes an opaque 500 carrying only a trace id.
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var unreadableRequest = exception is BadHttpRequestException { StatusCode: StatusCodes.Status400BadRequest };
        var problem = unreadableRequest
            ? ApiErrors.Validation("The request could not be read. Check the body format and supplied values.")
            : ApiErrors.Internal("An error occurred while processing your request.");
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status!.Value;

        if (!unreadableRequest)
        {
            logger.LogError(exception, "Unhandled exception handling {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return true;
    }
}
