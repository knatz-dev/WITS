using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using WITS.Api.Common;
using WITS.Common;

namespace WITS.Api.Auth;

public static class AuthenticationRateLimits
{
    public const string Policy = "authentication";

    public static void AddAuthenticationRateLimits(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsAuthentication(context)
                    ? RateLimitPartition.GetFixedWindowLimiter(Policy, _ => Window(120))
                    : RateLimitPartition.GetNoLimiter("other")),
                PartitionedRateLimiter.Create<HttpContext, string>(context => IsAuthentication(context)
                    ? RateLimitPartition.GetConcurrencyLimiter(Policy, _ => new ConcurrencyLimiterOptions { PermitLimit = 4, QueueLimit = 0 })
                    : RateLimitPartition.GetNoLimiter("other")));

            options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown", _ => Window(10)));

            options.OnRejected = async (context, _) =>
            {
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : 1;
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "rate_limited"));
                await TypedResults.Problem(ApiErrors.TooManyRequests("Too many authentication attempts. Try again later."))
                    .ExecuteAsync(context.HttpContext);
            };
        });
    }

    private static bool IsAuthentication(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == Policy;

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}
