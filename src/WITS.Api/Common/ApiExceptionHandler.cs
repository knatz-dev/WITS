using System.Diagnostics;
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
        // The client-visible trace id: the problem-details writer overwrites traceId with this same expression.
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        problem.Extensions["traceId"] = traceId;
        httpContext.Response.StatusCode = problem.Status!.Value;

        if (!unreadableRequest)
        {
            logger.LogError(exception, "Unhandled exception handling {Method} {Path} with trace id {TraceId}",
                httpContext.Request.Method, httpContext.Request.Path, traceId);
        }

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return true;
    }
}
