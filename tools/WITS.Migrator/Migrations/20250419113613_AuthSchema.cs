// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20250419113613AuthSchema : Migration
    {
        public override string MigrationId => "20250419113613";
        public override string Name => "AuthSchema";
        
        public override string UpScript => @"
            -- Create Users table
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Username TEXT NOT NULL UNIQUE,
                Email TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                FirstName TEXT,
                LastName TEXT,
                IsActive BOOLEAN NOT NULL DEFAULT 1,
                IsAdmin BOOLEAN NOT NULL DEFAULT 0,
                LastLoginAt DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create Roles table
            CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create UserRoles table
            CREATE TABLE IF NOT EXISTS UserRoles (
                UserId INTEGER NOT NULL,
                RoleId INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (UserId, RoleId),
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE,
                FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
            );

            -- Create Permissions table
            CREATE TABLE IF NOT EXISTS Permissions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            -- Create RolePermissions table
            CREATE TABLE IF NOT EXISTS RolePermissions (
                RoleId INTEGER NOT NULL,
                PermissionId INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (RoleId, PermissionId),
                FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
                FOREIGN KEY (PermissionId) REFERENCES Permissions(Id) ON DELETE CASCADE
            );

            -- Create RefreshTokens table
            CREATE TABLE IF NOT EXISTS RefreshTokens (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Token TEXT NOT NULL UNIQUE,
                ExpiresAt DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            -- Insert default roles
            INSERT INTO Roles (Name, Description) VALUES 
                ('Admin', 'System administrator with full access'),
                ('Manager', 'Project manager with elevated permissions'),
                ('User', 'Regular user with basic permissions');

            -- Insert default permissions
            INSERT INTO Permissions (Name, Description) VALUES 
                ('users.read', 'View user information'),
                ('users.write', 'Modify user information'),
                ('projects.read', 'View project information'),
                ('projects.write', 'Modify project information'),
                ('tickets.read', 'View ticket information'),
                ('tickets.write', 'Modify ticket information'),
                ('admin.access', 'Access administrative functions');";

        public override string DownScript => @"
            DROP TABLE IF EXISTS RolePermissions;
            DROP TABLE IF EXISTS Permissions;
            DROP TABLE IF EXISTS UserRoles;
            DROP TABLE IF EXISTS Roles;
            DROP TABLE IF EXISTS RefreshTokens;
            DROP TABLE IF EXISTS Users;";
    }
}
