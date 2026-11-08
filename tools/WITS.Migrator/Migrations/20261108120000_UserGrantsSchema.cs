using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20261108120000UserGrantsSchema : Migration
    {
        public override string MigrationId => "20261108120000";
        public override string Name => "UserGrantsSchema";

        public override string UpScript => @"
            CREATE TABLE IF NOT EXISTS UserGrants (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId INTEGER NOT NULL,
                Scope TEXT NOT NULL,
                ScopeId INTEGER NOT NULL,
                Permission TEXT NOT NULL,
                Created DATETIME NOT NULL,
                UNIQUE(UserId, Scope, ScopeId, Permission),
                FOREIGN KEY (UserId) REFERENCES User(Id)
            );";

        public override string DownScript => "DROP TABLE IF EXISTS UserGrants;";
    }
}