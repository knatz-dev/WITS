// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  @ 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Text;
using System.Diagnostics;
using WITS.Services;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Factory;
using WITS.Data.Repository;
using WITS.Common;
using WITS.Api.Common;
using WITS.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Configure JWT Authentication
builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(nameof(JwtSettings)))
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer), "JwtSettings:Issuer must not be blank.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "JwtSettings:Audience must not be blank.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.SecurityKey)
        && Encoding.UTF8.GetByteCount(settings.SecurityKey) >= 32,
        "JwtSettings:SecurityKey must contain at least 32 UTF-8 bytes for HS256.")
    .Validate(settings => builder.Environment.IsDevelopment()
        || settings.SecurityKey != "YourLongSecretKeyHere_Replace_In_Production",
        "JwtSettings:SecurityKey must replace the sample key outside Development.")
    .Validate(settings => settings.ExpirationMinutes > 0, "JwtSettings:ExpirationMinutes must be positive.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, settings) =>
    {
        var jwtSettings = settings.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.SecurityKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>(
                    "reason", context.AuthenticateFailure is null ? "missing_bearer" : "invalid_bearer"));
                context.HandleResponse();
                context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.HttpContext.Response.Headers.WWWAuthenticate = "Bearer";
                await WriteProblemAsync(context.HttpContext, ApiErrors.Unauthorized("Authentication is required to access this resource."));
            },
            OnForbidden = async context =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await WriteProblemAsync(context.HttpContext, ApiErrors.Forbidden("You do not have permission to access this resource."));
            }
        };
    });

static async Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
{
    await context.RequestServices.GetRequiredService<IProblemDetailsService>()
        .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
}

builder.Services.AddAuthorization();
builder.Services.AddAuthenticationRateLimits();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginAttemptTracker>();
builder.Services.AddSingleton<RefreshTokenStore>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<IDbConnectionFactory>(services =>
{
    var connectionString = DatabaseConfiguration.GetConnectionString(services.GetRequiredService<IConfiguration>());
    services.GetRequiredService<ILogger<SQliteConnectionFactory>>()
        .LogInformation("Database: {DatabasePath}", new SqliteConnectionStringBuilder(connectionString).DataSource);
    return new SQliteConnectionFactory(connectionString);
});
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped(typeof(IGenericRepository<,>), typeof(GenericRepository<,>));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITicketService, TicketService>();

WebApplication app = builder.Build();
_ = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
await DatabaseConfiguration.ValidateAccessAsync(app.Services.GetRequiredService<IDbConnectionFactory>());

// Configure the HTTP request pipeline.
// The scope must wrap the exception handler so failure logs carry the same trace id the client receives.
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("RequestCorrelation");
app.Use(async (context, next) =>
{
    using (requestLogger.BeginScope("Request {TraceId}", context.TraceIdentifier))
    {
        var started = Stopwatch.GetTimestamp();
        var route = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "(unmatched)";
        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            OperationalSignals.Requests.Add(1, new TagList
            {
                { "http.route", route },
                { "http.response.status_code", context.Response.StatusCode }
            });
            if (elapsed.TotalSeconds >= 1)
            {
                requestLogger.LogWarning("Slow request {Method} {Route} returned {StatusCode} after {ElapsedMs} ms with trace id {TraceId}",
                    context.Request.Method, route, context.Response.StatusCode, elapsed.TotalMilliseconds,
                    Activity.Current?.Id ?? context.TraceIdentifier);
            }
        }
    }
});

app.UseExceptionHandler();

// Minimal-API binding failures end the request with a bare 400, so give them the shared problem shape.
app.Use(async (context, next) =>
{
    await next(context);
    if (context.Response.StatusCode == StatusCodes.Status400BadRequest
        && !context.Response.HasStarted
        && string.IsNullOrEmpty(context.Response.ContentType))
    {
        await WriteProblemAsync(context, ApiErrors.Validation("The request could not be read. Check the body format and supplied values."));
    }
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGroup("api/v1/").RegisterEndpointDefinitions();

await app.RunAsync();
