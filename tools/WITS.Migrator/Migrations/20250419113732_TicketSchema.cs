// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
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
            -- Create TicketTypes table (matches WITS.Data.Entity.TicketType)
            CREATE TABLE IF NOT EXISTS TicketTypes (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                LastModified DATETIME
            );

            -- Create TicketPriorities table (matches WITS.Data.Entity.TicketPriority)
            CREATE TABLE IF NOT EXISTS TicketPriorities (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                LastModified DATETIME
            );

            -- Create TicketStatuses table (matches WITS.Data.Entity.TicketStatus)
            CREATE TABLE IF NOT EXISTS TicketStatuses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                LastModified DATETIME
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
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                FOREIGN KEY (TypeId) REFERENCES TicketTypes(Id),
                FOREIGN KEY (PriorityId) REFERENCES TicketPriorities(Id),
                FOREIGN KEY (StatusId) REFERENCES TicketStatuses(Id),
                FOREIGN KEY (ProjectId) REFERENCES Projects(Id),
                FOREIGN KEY (CreatedByUserId) REFERENCES User(Id),
                FOREIGN KEY (AssignedToUserId) REFERENCES User(Id)
            );

            -- Create TicketComments table
            CREATE TABLE IF NOT EXISTS TicketComments (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TicketId INTEGER NOT NULL,
                UserId INTEGER NOT NULL,
                Content TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (TicketId) REFERENCES Tickets(Id),
                FOREIGN KEY (UserId) REFERENCES User(Id)
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
                FOREIGN KEY (UploadedByUserId) REFERENCES User(Id)
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
                FOREIGN KEY (ChangedByUserId) REFERENCES User(Id)
            );

            -- Insert default ticket types
            INSERT INTO TicketTypes (Name) VALUES
                ('Bug'),
                ('Feature'),
                ('Task'),
                ('Improvement');

            -- Insert default priorities
            INSERT INTO TicketPriorities (Name) VALUES
                ('Low'),
                ('Medium'),
                ('High'),
                ('Critical');

            -- Insert default statuses
            INSERT INTO TicketStatuses (Name) VALUES
                ('Open'),
                ('In Progress'),
                ('Review'),
                ('Resolved'),
                ('Closed');";

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
