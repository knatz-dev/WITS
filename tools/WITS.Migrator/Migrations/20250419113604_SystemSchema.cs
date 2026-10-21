// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20250419113604SystemSchema : Migration
    {
        public override string MigrationId => "20250419113604";
        public override string Name => "SystemSchema";
        
        public override string UpScript => @"
            -- TODO: Add your migration SQL here
            -- Example: CREATE TABLE IF NOT EXISTS TableName (
            --     Id INTEGER PRIMARY KEY AUTOINCREMENT,
            --     CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            -- )";

        public override string DownScript => @"
            -- TODO: Add your rollback SQL here
            -- Example: DROP TABLE IF EXISTS TableName";
    }
}
