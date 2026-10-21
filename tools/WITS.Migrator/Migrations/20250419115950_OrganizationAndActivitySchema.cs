// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20250419115950OrganizationAndActivitySchema : Migration
    {
        public override string MigrationId => "20250419115950";
        public override string Name => "OrganizationAndActivitySchema";
        
        public override string UpScript => @"
            -- Create Organizations table
            CREATE TABLE IF NOT EXISTS Organizations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                Website TEXT,
                Email TEXT,
                Phone TEXT,
                Address TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CreatedByUserId INTEGER NOT NULL,
                FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id)
            );

            -- Create OrganizationMembers table
            CREATE TABLE IF NOT EXISTS OrganizationMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrganizationId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                UNIQUE(OrganizationId, UserId)
            );

            -- Create Activities table
            CREATE TABLE IF NOT EXISTS Activities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Type TEXT NOT NULL,
                Description TEXT NOT NULL,
                EntityType TEXT NOT NULL,
                EntityId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                OrganizationId INTEGER NOT NULL,
                ProjectId INTEGER,
                TicketId INTEGER,
                Data TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                FOREIGN KEY (OrganizationId) REFERENCES Organizations(Id),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id)
            );

            -- Create ActivityComments table
            CREATE TABLE IF NOT EXISTS ActivityComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ActivityId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (ActivityId) REFERENCES Activities(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            -- Create Notifications table
            CREATE TABLE IF NOT EXISTS Notifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Type TEXT NOT NULL,
                Title TEXT NOT NULL,
                Message TEXT NOT NULL,
                IsRead BOOLEAN NOT NULL DEFAULT 0,
                ActivityId INTEGER,
                ProjectId INTEGER,
                TicketId INTEGER,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES Users(Id),
                FOREIGN KEY (ActivityId) REFERENCES Activities(Id),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id)
            );

            -- Add OrganizationId to existing tables
            ALTER TABLE Projects ADD COLUMN OrganizationId INTEGER NOT NULL DEFAULT 1 REFERENCES Organizations(Id);
            ALTER TABLE Users ADD COLUMN DefaultOrganizationId INTEGER REFERENCES Organizations(Id);

            -- Create default organization
            INSERT INTO Organizations (Id, Name, Description, CreatedByUserId) VALUES 
                (1, 'Default Organization', 'Default organization created by system', 1);";

        public override string DownScript => @"
            -- Remove organization references from existing tables
            CREATE TABLE Projects_Temp AS SELECT Id, Name, Description, CreatedAt, UpdatedAt, CreatedByUserId FROM Projects;
            DROP TABLE Projects;
            ALTER TABLE Projects_Temp RENAME TO Projects;

            CREATE TABLE Users_Temp AS SELECT Id, Username, Email, PasswordHash, FirstName, LastName, IsActive, IsAdmin, LastLoginAt, CreatedAt, UpdatedAt FROM Users;
            DROP TABLE Users;
            ALTER TABLE Users_Temp RENAME TO Users;

            -- Drop new tables
            DROP TABLE IF EXISTS Notifications;
            DROP TABLE IF EXISTS ActivityComments;
            DROP TABLE IF EXISTS Activities;
            DROP TABLE IF EXISTS OrganizationMembers;
            DROP TABLE IF EXISTS Organizations;";
    }
}
