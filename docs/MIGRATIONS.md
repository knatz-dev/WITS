# Migration baseline and recovery

## Supported starting points

The maintainer selected **fresh databases only** on 2026-10-24. No conversion of
existing legacy application data is supported. Provision a new, explicitly
configured SQLite database and run the compiled migration chain, then the seeder
for development accounts.

The fresh canonical baseline consists of these migration IDs, in order:

1. `20250419113604` — SystemSchema
2. `20250419113613` — AuthSchema
3. `20250419113619` — ProjectSchema
4. `20250419113732` — TicketSchema
5. `20250419115950` — OrganizationAndActivitySchema
6. `20261023090000` — ConstraintHardening

A partially completed fresh setup can resume when its applied history is a
contiguous prefix of the build's migration chain. Its stored names and SQL must
match the build; only line-ending differences are ignored. A current database is
a no-op. Unknown migration IDs, gaps, or edited applied scripts stop `migrate`
before pending SQL runs. Application objects without history, including an empty
history table, are rejected rather than treated as a fresh database.

Older `Users`/`Organizations` layouts and earlier edits under these same migration
IDs are unsupported. Use a new path; do not delete data or rewrite history to make
an old database appear compatible. History validation is not a full inspection of
manual schema drift or proof that recorded SQL was actually executed.

## Future changes

Applied migration IDs, names, and up-scripts are immutable. Add a new migration
with an ID greater than the existing IDs instead of editing an applied script.
Preserve existing identifiers, relationships, application data, and history.
Verify fresh setup, populated forward progression, repeat execution, and failure
atomicity with the actual SQLite provider before shipping the new migration.

Each migration commits independently. Its SQL changes and history insertion share
one transaction; failure leaves that migration unapplied and earlier committed
migrations intact. Run one maintenance process at a time with the API stopped.

## Rollback policy

**All six baseline migrations are forward-only.** Their down-scripts either drop
application data or fail to restore the previous schema completely. In particular,
ConstraintHardening leaves its version columns behind, and
OrganizationAndActivitySchema leaves added columns behind. The tool refuses these
rollbacks without executing the down-script or deleting history.

Rollback also refuses an applied migration while any later migration is applied.
A migration not applied is a no-op. Future migrations may opt into
`SupportsRollback` only after tests prove that their down-script preserves
application data, restores the prior schema, and permits reapplication. The
latest-migration check, down-script, and history deletion run in one transaction;
a failed down-script or history deletion rolls back the whole operation.

## Recovering a failed setup or deployment

1. Keep the failed database and original error for diagnosis. Do not mark the
   failed migration applied or remove earlier history entries.
2. For an operational failure such as a lock or unavailable storage, correct the
   cause and rerun the same build's `migrate` command against the same target.
3. For a data-constraint failure, resolve the conflicting data deliberately on an
   isolated copy first. The tool never chooses which accounts or records to
   discard. Verify the correction and retry the pending migration; previous
   history must remain unchanged.
4. For incompatible history or an unsuccessful deployed change, use the matching
   application build and a verified pre-change backup, following the stopped-API
   restore procedure. For this fresh-only scope, provisioning a new database at a
   different path is also supported. Retain the original database rather than
   overwriting it.

See [backup and restore](BACKUP.md). The executable smoke suite checks populated
baseline progression, rejected history/rollback without database changes,
constraint-failure correction/retry, and transactional opt-in rollback with an
isolated index migration. Production data is never used by those checks.
