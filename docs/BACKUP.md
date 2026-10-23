# Backup and restore

The SQLite database is the only durable store today. Attachments are not implemented, so there is no separate blob store to back up.

## Prerequisites
- Keep the JWT signing key (`JwtSettings:SecurityKey`) separately from the database file. A restored database is useless without its signing key (and rotating it invalidates tokens).
- Know the path used by every process. All three (API, migrator, seeder) must use the same absolute `ConnectionStrings:DefaultConnection` for a given environment.
- Confirm the schema version by running `dotnet run --project tools/WITS.Migrator -- list-migrations` against the target database.

## Take a backup

### Online (while the API is running)
SQLite supports online backups safely if the database is not in the middle of a WAL checkpoint in an unexpected state. From the repository root, with the environment set:

```powershell
$database = "C:\WITSData\Production\WITS.db"
# Copy while the file is live; prefer VACUUM/backup via sqlite3 if available.
Copy-Item -Path $database -Destination "C:\Backups\WITS-$(Get-Date -Format yyyyMMddHHmmss).db"
# If WAL mode is in use, also capture -wal/-shm only if copying a consistent snapshot.
```

For stronger guarantees, use the SQLite CLI: `sqlite3 "$database" ".backup 'C:\Backups\WITS.db'"`. This takes a consistent snapshot without stopping the process.

### Offline (safest)
Stop the API instance, copy the database file and any `-wal`/`-shm` files together, then start the API again.

## Restore

1. **Stop the API.** Never restore over a running database.
2. **Replace the file.** Copy the backup to the configured path. Restore `-wal`/`-shm` only if they were captured together with a consistent snapshot; otherwise delete them so SQLite recreates them.
3. **Verify integrity.** Run `sqlite3 <path> "PRAGMA quick_check;"` and expect `ok`.
4. **Apply migrations.** Run `dotnet run --project tools/WITS.Migrator -- migrate` with the same connection string so the restored database matches the deployed code version.
5. **Start the API.** Startup validation re-checks JWT settings and database access; if settings changed, provide the original signing key.
6. **Smoke check.** Hit a read endpoint with a valid token or run `dotnet run --project tests/WITS.Api.SmokeTests` against an isolated fixture. Do not test against the restored production database with the live test suite.

## Retention
- Keep at least 7 daily and 4 weekly backups.
- Encrypt backups at rest and in transit.
- Test a restore into an isolated environment on a schedule (see the automated drill below).

## Automated drill
The smoke suite includes `BackupRestoreChecks`, which takes an online WAL-mode backup of an isolated SQLite database, restores it to a new path, passes `PRAGMA quick_check`, confirms migration history survives, proves later writes are excluded from the snapshot, and verifies the restored account still authenticates. It cleans up temporary files.
