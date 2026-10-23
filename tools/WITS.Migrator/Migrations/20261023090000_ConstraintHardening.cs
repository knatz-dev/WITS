// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  © 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using Microsoft.Data.Sqlite;

namespace WITS.Migrator.Migrations
{
    public class Migration20261023090000ConstraintHardening : Migration
    {
        public override string MigrationId => "20261023090000";
        public override string Name => "ConstraintHardening";

        public override string UpScript => @"
            -- Account identifiers are global to the installation, not scoped to an organisation.
            -- NOCASE folds ASCII case, so 'Alice' and 'alice' are the same username and email address.
            CREATE UNIQUE INDEX IF NOT EXISTS idx_User_Username_NoCase
                ON User(Username COLLATE NOCASE);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_User_EmailAddress_NoCase
                ON User(EmailAddress COLLATE NOCASE);";

        public override string DownScript => @"
            DROP INDEX IF EXISTS idx_User_EmailAddress_NoCase;
            DROP INDEX IF EXISTS idx_User_Username_NoCase;";
    }
}
