// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20250419113732TicketSchema : Migration
    {
        public override string MigrationId => "20250419113732";
        public override string Name => "TicketSchema";
        
        public override string UpScript => @"
            -- Create TicketTypes table
            CREATE TABLE IF NOT EXISTS TicketTypes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create TicketPriorities table
            CREATE TABLE IF NOT EXISTS TicketPriorities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create TicketStatuses table
            CREATE TABLE IF NOT EXISTS TicketStatuses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create Tickets table
            CREATE TABLE IF NOT EXISTS Tickets (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT,
                TypeId INTEGER NOT NULL,
                PriorityId INTEGER NOT NULL,
                StatusId INTEGER NOT NULL,
                ProjectId INTEGER NOT NULL,
                CreatedByUserId INTEGER NOT NULL,
                AssignedToUserId INTEGER,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                DueDate DATETIME,
                FOREIGN KEY (TypeId) REFERENCES TicketTypes(Id),
                FOREIGN KEY (PriorityId) REFERENCES TicketPriorities(Id),
                FOREIGN KEY (StatusId) REFERENCES TicketStatuses(Id),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (CreatedByUserId) REFERENCES Users(Id),
                FOREIGN KEY (AssignedToUserId) REFERENCES Users(Id)
            );

            -- Create TicketComments table
            CREATE TABLE IF NOT EXISTS TicketComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TicketId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );

            -- Create TicketAttachments table
            CREATE TABLE IF NOT EXISTS TicketAttachments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TicketId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileSize INTEGER NOT NULL,
                ContentType TEXT NOT NULL,
                UploadedByUserId INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id),
                FOREIGN KEY (UploadedByUserId) REFERENCES Users(Id)
            );

            -- Create TicketHistory table
            CREATE TABLE IF NOT EXISTS TicketHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TicketId INTEGER NOT NULL,
                ChangedByUserId INTEGER NOT NULL,
                FieldName TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id),
                FOREIGN KEY (ChangedByUserId) REFERENCES Users(Id)
            );

            -- Insert default ticket types
            INSERT INTO TicketTypes (Name, Description) VALUES 
                ('Bug', 'A problem that needs to be fixed'),
                ('Feature', 'A new feature to be implemented'),
                ('Task', 'A general task to be completed'),
                ('Improvement', 'An enhancement to existing functionality');

            -- Insert default priorities
            INSERT INTO TicketPriorities (Name, Description) VALUES 
                ('Low', 'Low priority issue'),
                ('Medium', 'Medium priority issue'),
                ('High', 'High priority issue'),
                ('Critical', 'Critical issue requiring immediate attention');

            -- Insert default statuses
            INSERT INTO TicketStatuses (Name, Description) VALUES 
                ('Open', 'Ticket is open and needs to be addressed'),
                ('In Progress', 'Ticket is being worked on'),
                ('Review', 'Ticket is under review'),
                ('Resolved', 'Ticket has been resolved'),
                ('Closed', 'Ticket has been closed');";

        public override string DownScript => @"
            DROP TABLE IF EXISTS TicketHistory;
            DROP TABLE IF EXISTS TicketAttachments;
            DROP TABLE IF EXISTS TicketComments;
            DROP TABLE IF EXISTS Tickets;
            DROP TABLE IF EXISTS TicketStatuses;
            DROP TABLE IF EXISTS TicketPriorities;
            DROP TABLE IF EXISTS TicketTypes;";
    }
}
