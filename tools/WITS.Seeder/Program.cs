// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using WITS.Data;
using WITS.Data.Factory;
using WITS.Seeder;

Console.WriteLine("WITS Seeder");
Console.WriteLine("Initializing database...");

using var config = DatabaseConfiguration.LoadToolConfiguration(args);
var connectionString = DatabaseConfiguration.GetConnectionString(config);
Console.WriteLine($"Database: {new SqliteConnectionStringBuilder(connectionString).DataSource}");

try
{
    DbInitializer.Initialize(new SQliteConnectionFactory(connectionString));
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"Seeding failed: {ex}");
    return 1;
}

return 0;
