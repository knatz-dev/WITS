// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
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
            -- Create OrganizationMembers table (canonical tables are Organisation and User)
            CREATE TABLE IF NOT EXISTS OrganizationMembers (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrganizationId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Role TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (OrganizationId) REFERENCES Organisation(Id),
                FOREIGN KEY (UserId) REFERENCES User(Id),
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
                FOREIGN KEY (UserId) REFERENCES User(Id),
                FOREIGN KEY (OrganizationId) REFERENCES Organisation(Id),
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
                FOREIGN KEY (UserId) REFERENCES User(Id)
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
                FOREIGN KEY (UserId) REFERENCES User(Id),
                FOREIGN KEY (ActivityId) REFERENCES Activities(Id),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id)
            );

            -- Add canonical organisation references to existing tables
            ALTER TABLE Projects ADD COLUMN OrganizationId INTEGER NOT NULL DEFAULT 1 REFERENCES Organisation(Id);
            ALTER TABLE User ADD COLUMN DefaultOrganizationId INTEGER REFERENCES Organisation(Id);

            -- Create default organisation
            INSERT OR IGNORE INTO Organisation (Id, Name, Description, Code, Created) VALUES
                (1, 'Default Organization', 'Default organization created by system', 'DEFAULT', CURRENT_TIMESTAMP);";

        public override string DownScript => @"
            DROP TABLE IF EXISTS Notifications;
            DROP TABLE IF EXISTS ActivityComments;
            DROP TABLE IF EXISTS Activities;
            DROP TABLE IF EXISTS OrganizationMembers;
            DROP TABLE IF EXISTS Organizations;";
    }
}
