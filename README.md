# wits
Work Item Tracking System

## Database target

The API, migrator, and seeder all use **`ConnectionStrings:DefaultConnection`**.
Set one absolute SQLite path for the environment and pass the same setting to all
three processes. No database file is selected by default. Missing/relative paths
fail with an error instead of selecting a file based on the working directory.
To keep existing data, use its current absolute path.

PowerShell example, from the repository root:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
$database = Join-Path $PWD "APP_DATA/WITS.Development.db"
$env:ConnectionStrings__DefaultConnection = 'Data Source="' + $database + '"'

dotnet restore WITS.slnx
dotnet build WITS.slnx --no-restore
dotnet run --project tools/WITS.Migrator -- list-migrations
dotnet run --project tools/WITS.Migrator -- migrate
dotnet run --project tools/WITS.Seeder
dotnet run --project src/WITS.Api --no-launch-profile -- --urls http://localhost:5351
```

For another environment, select a distinct path (for example,
`C:\WITSData\Production\WITS.db`) and set `DOTNET_ENVIRONMENT=Production` in each
process's deployment configuration. Each process reports its selected database
path. The API fixes the target at startup; restart it after changing the setting.
In-memory SQLite is also accepted for isolated tests.

### Configuration precedence

From lowest to highest: `appsettings.json`, `appsettings.{Environment}.json`,
environment variables, command-line options. The API retains the standard ASP.NET
Core providers, including development user secrets if configured.

The tools load JSON beside their executable, independent of the working directory;
their project files copy `appsettings*.json` into build/publish output. The API
loads JSON from its content root. Configure the same absolute target in each
process when using environment JSON, or use the shared environment variable above.
Environment selection is `--environment`, then `DOTNET_ENVIRONMENT`, then
`ASPNETCORE_ENVIRONMENT`, defaulting to `Production`.

CLI overrides go **after the migrator command and its positional arguments**:

```powershell
dotnet run --project tools/WITS.Migrator -- list-tables --environment Development --ConnectionStrings:DefaultConnection 'Data Source=C:\WITSData\Development\WITS.db'
dotnet run --project tools/WITS.Migrator -- rollback 20250419113604 --ConnectionStrings:DefaultConnection 'Data Source=C:\WITSData\Development\WITS.db'
dotnet run --project tools/WITS.Seeder -- --ConnectionStrings:DefaultConnection 'Data Source=C:\WITSData\Development\WITS.db'
```

Migration apply/rollback use the exact connection string selected by the command
entrypoint. Migration source creation/removal still resolves `Migrations/` against
the working directory; run `create`/`remove` from `tools/WITS.Migrator`.

**Current setup limitations:** migration application still has a count-cast/pending
check defect, and seeding has transaction/schema defects. Database selection is
verified; successful schema setup and seed insertion remain incomplete.

## Startup validation

Before serving requests, the API checks:

- `JwtSettings:Issuer` and `JwtSettings:Audience` are nonblank.
- `JwtSettings:SecurityKey` is nonblank and at least **32 UTF-8 bytes** for HS256.
- The sample signing key is rejected outside `Development`. Supply a randomly
  generated deployment key through `JwtSettings__SecurityKey` or your secret store.
- `JwtSettings:ExpirationMinutes` is a positive integer.
- The configured SQLite database can be opened and written, including access to
  its journal directory. A metadata write is rolled back; existing data, schema,
  and metadata are preserved. A missing database may be created if the configured
  SQLite mode permits it. This does not create or validate the application schema.

Invalid settings stop startup and identify the configuration key to correct.
Token signing and verification use the same validated settings and UTF-8 key
encoding. Previously issued tokens using non-ASCII keys need to be reissued
because older code used ASCII encoding. Restart after changing JWT or database
settings.

## Error responses

Every expected failure answers with `application/problem+json` carrying `status`,
`title`, and `detail`:

- Unreadable or invalid input → **400** (`Bad Request`), including malformed
  request bodies, which the framework otherwise ends with an empty 400.
- Missing or invalid bearer token → **401** (`Unauthorized`), keeping
  `WWW-Authenticate`; rejected login credentials answer the same way.
- Authenticated but disallowed → **403** (`Forbidden`).
- Unknown resource → **404** (`Not Found`).
- Duplicate registration → **409** (`Conflict`).
- Anything unhandled → **500** (`Internal Server Error`) with only a `traceId`;
  the exception is logged server-side and never included in the response.

## Verification

```sh
dotnet run --project tests/WITS.Api.SmokeTests
```

This executable smoke check covers startup validation, error responses, DI, and database targeting with isolated
fixtures. See [its README](tests/WITS.Api.SmokeTests/README.md) for scope. Run it
explicitly; `dotnet test` does not execute it.
