// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20250419113619ProjectSchema : Migration
    {
        public override string MigrationId => "20250419113619";
        public override string Name => "ProjectSchema";
        
        public override string UpScript => @"
            -- Create Projects table
            CREATE TABLE IF NOT EXISTS Projects (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CreatedByUserId INTEGER NOT NULL,
                FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
            );

            -- Create ProjectMembers table
            CREATE TABLE IF NOT EXISTS ProjectMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                UNIQUE(ProjectId, UserId)
            );

            -- Create ProjectTasks table
            CREATE TABLE IF NOT EXISTS ProjectTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT,
                Status TEXT NOT NULL,
                Priority TEXT NOT NULL,
                AssignedToUserId INTEGER,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                DueDate DATETIME,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (AssignedToUserId) REFERENCES Users(Id)
            );

            -- Create ProjectComments table
            CREATE TABLE IF NOT EXISTS ProjectComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProjectId INTEGER NOT NULL,
                TaskId INTEGER,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (TaskId) REFERENCES ProjectTasks(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );";

        public override string DownScript => @"
            DROP TABLE IF EXISTS ProjectComments;
            DROP TABLE IF EXISTS ProjectTasks;
            DROP TABLE IF EXISTS ProjectMembers;
            DROP TABLE IF EXISTS Projects;";
    }
}
