// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  @ 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Text;
using WITS.Services;
using WITS.Data;
using WITS.Data.Contracts;
using WITS.Data.Factory;
using WITS.Data.Repository;
using WITS.Common;
using WITS.Api.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddProblemDetails();

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
    });

builder.Services.AddAuthorization();

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
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGroup("api/v1/").RegisterEndpointDefinitions();

await app.RunAsync();
