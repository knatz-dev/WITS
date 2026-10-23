// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
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
            -- Canonical user table, matching WITS.Data.Entity.User.
            CREATE TABLE IF NOT EXISTS User (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrganisationId INTEGER NOT NULL,
                FirstName TEXT NOT NULL,
                LastName TEXT NOT NULL,
                Username TEXT NOT NULL UNIQUE,
                EmailAddress TEXT NOT NULL UNIQUE,
                PhoneNumber TEXT,
                PictureUrl TEXT,
                PasswordHash TEXT,
                PasswordSalt TEXT,
                IsLocked BOOLEAN NOT NULL DEFAULT 0,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL,
                LastModified DATETIME,
                FOREIGN KEY (OrganisationId) REFERENCES Organisation(Id)
            );

            -- Create Roles table (matches WITS.Data.Entity.Role)
            CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE,
                Description TEXT,
                Code TEXT NOT NULL,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                LastModified DATETIME
            );

            -- Create UserRoles table
            CREATE TABLE IF NOT EXISTS UserRoles (
                UserId INTEGER NOT NULL,
                RoleId INTEGER NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (UserId, RoleId),
                FOREIGN KEY (UserId) REFERENCES User(Id) ON DELETE CASCADE,
                FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE
            );

            -- Create Permissions table (matches WITS.Data.Entity.Permission)
            CREATE TABLE IF NOT EXISTS Permissions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Action TEXT NOT NULL UNIQUE,
                Description TEXT,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                Created DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                LastModified DATETIME
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
                FOREIGN KEY (UserId) REFERENCES User(Id) ON DELETE CASCADE
            );

            -- Insert default roles
            INSERT INTO Roles (Name, Description, Code) VALUES
                ('Admin', 'System administrator with full access', 'ADMIN'),
                ('Manager', 'Project manager with elevated permissions', 'MANAGER'),
                ('User', 'Regular user with basic permissions', 'USER');

            -- Insert default permissions
            INSERT INTO Permissions (Action, Description) VALUES
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
            DROP TABLE IF EXISTS User;
            DROP TABLE IF EXISTS Users;";
    }
}
