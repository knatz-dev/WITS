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
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddProblemDetails();

// Configure JWT Authentication
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(nameof(JwtSettings))
);


builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        JwtSettings? jwtSettings = builder.Configuration
                                          .GetSection(nameof(JwtSettings))
                                          .Get<JwtSettings>();

        if (jwtSettings is null)
        {
            throw new InvalidOperationException("JWT settings are not configured properly.");
        }

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.ASCII.GetBytes(jwtSettings.SecurityKey))
        };
    });

builder.Services.AddAuthorization();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddScoped<IDbConnectionFactory>(services => new SQliteConnectionFactory(
    services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("The connection string 'DefaultConnection' is not configured.")));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped(typeof(IGenericRepository<,>), typeof(GenericRepository<,>));
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITicketService, TicketService>();

WebApplication app = builder.Build();

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
