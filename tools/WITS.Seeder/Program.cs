// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using WITS.Data.Factory;
using WITS.Seeder;

Console.WriteLine("WITS Migrator");
Console.WriteLine("Initializing database...");

// Load configuration from various sources
var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

// Get database connection string from configuration
var connectionString = config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection not found in configuration");

// Ensure database directory exists
var dbPath = Path.GetDirectoryName(connectionString.Split('=')[1]);
if (!string.IsNullOrEmpty(dbPath) && !Directory.Exists(dbPath))
{
    Directory.CreateDirectory(dbPath);
}

DbInitializer.Initialize(new SQliteConnectionFactory(connectionString));

Console.ReadLine();
