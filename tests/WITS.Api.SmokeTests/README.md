# API startup smoke check

From the repository root:

```sh
dotnet run --project tests/WITS.Api.SmokeTests
```

This executable check starts the real API with in-memory SQLite, requests a protected route, and resolves the database factory, unit of work, repositories, and application services. It checks the configured connection string and unit-of-work scope isolation. Failed assertions or startup failures return a nonzero exit code.

Startup-validation checks cover missing/blank issuer, audience and signing key, undersized keys, non-development sample keys, nonpositive/malformed token lifetimes, and invalid database configuration. Real-host checks reject read-only, missing (`Mode=ReadWrite`), corrupt, and unusable-directory database targets before any request is sent. A successful startup preserves a fixture's schema, data, and `user_version`. Boundary-length ASCII/UTF-8 keys issue tokens that validate against both the actual bearer settings and the expected UTF-8 signing key.

It also runs the real migrator and seeder against temporary SQLite files and checks:

- Environment JSON, environment-variable overrides, and CLI overrides select the same file as the API.
- `DOTNET_ENVIRONMENT` takes precedence over `ASPNETCORE_ENVIRONMENT`; CLI `--environment` takes precedence over both in the tools.
- Tool settings come from the executable directory even with conflicting JSON in the working directory.
- Missing/relative paths are rejected; absolute paths containing spaces, equals signs, and semicolons work.
- Migration bootstrap, history inspection, and rollback use the explicit target, without touching a decoy database.

The seeder check verifies the file it opens, not successful data insertion. The migration-application check verifies history-table bootstrap on the selected file; the existing count-cast defect still blocks up-scripts. A synthetic history row exercises rollback independently. These known defects remain separate checklist items.

Run this explicitly; it is not a `dotnet test` project. Temporary databases and environment JSON fixtures are removed after the run. Registration/login and production data/filesystem permissions are not exercised.
