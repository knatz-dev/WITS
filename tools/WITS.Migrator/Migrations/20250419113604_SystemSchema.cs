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
            -- Create Organisation table
            CREATE TABLE IF NOT EXISTS Organisation (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                Created TEXT NOT NULL,
                LastModified TEXT,
                Name TEXT NOT NULL,
                Code TEXT NOT NULL,
                Description TEXT
            );

            -- Create unique index on Code
            CREATE UNIQUE INDEX IF NOT EXISTS idx_Organisation_Code ON Organisation(Code);
        ";

        public override string DownScript => @"
            DROP INDEX IF EXISTS idx_Organisation_Code;
            DROP TABLE IF EXISTS Organisation;
        ";
    }
}
