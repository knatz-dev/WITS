using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20261107120000InvitationSchema : Migration
    {
        public override string MigrationId => "20261107120000";
        public override string Name => "InvitationSchema";

        public override string UpScript => @"
            CREATE TABLE IF NOT EXISTS Invitations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrganisationId INTEGER NOT NULL,
                Code TEXT NOT NULL UNIQUE,
                CreatedByUserId INTEGER NOT NULL,
                UsedByUserId INTEGER,
                ExpiresAt DATETIME NOT NULL,
                Created DATETIME NOT NULL,
                LastModified DATETIME,
                IsDeleted BOOLEAN NOT NULL DEFAULT 0,
                RowVersion INTEGER NOT NULL DEFAULT 1,
                UsedAt DATETIME,
                IsRevoked BOOLEAN NOT NULL DEFAULT 0,
                FOREIGN KEY (OrganisationId) REFERENCES Organisation(Id)
            );";

        public override string DownScript => "DROP TABLE IF EXISTS Invitations;";
    }
}