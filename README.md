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
- Authentication request limit reached → **429** (`Too Many Requests`) with
  `Retry-After` in seconds.
- Anything unhandled → **500** (`Internal Server Error`) with only a `traceId`;
  the exception is logged server-side and never included in the response.

## Password storage

Passwords are hashed with **PBKDF2-HMAC-SHA256**, 600,000 iterations, and a
fresh 16-byte random salt per password. `PasswordHash` stores
`PBKDF2$<iterations>$<base64 hash>` and `PasswordSalt` stores the base64 salt.
The cost is recorded with the value, so iterations can be raised later while
existing hashes keep verifying.

The previous HMAC-based scheme could never verify a password: hashing used a
random HMAC key while verification used the Base64 *text* of that key, so the
keys never matched. There is therefore nothing to migrate — legacy rows and the
seeder's placeholder hashes fail verification and those accounts need a password
reset once the recovery flow exists. Unrecognized or damaged stored values fail
closed with `false` instead of throwing.

## Authentication abuse controls

Login and registration share these process-local limits, applied before request
body binding and service execution:

- **10 requests per source IP per one-minute fixed window**, across both routes.
- **120 requests per process per one-minute fixed window**, across all source IPs.
- **4 concurrent authentication requests**, with no waiting queue.

Excess traffic receives `429 application/problem+json` and `Retry-After`. The
process budget is checked first, so IP/concurrency rejections also consume that
budget. Fixed windows can admit bursts at a window boundary. Missing remote
addresses share one bucket; IPv4 and IPv4-mapped IPv6 addresses share a bucket.
The limiter uses `Connection.RemoteIpAddress`, not a request-supplied forwarding
header. Behind a proxy, clients share the proxy's bucket until explicitly trusted
forwarded-header handling or proxy-side enforcement is configured. Do not trust
arbitrary `X-Forwarded-For` values.

For each username, **five failed credential checks within a 15-minute observation
window** block further checks for **15 minutes from the fifth failure**. Unknown
usernames follow the same rule. Checks for the same account are serialized:
concurrent attempts are rejected rather than queued. Throttle keys ignore case
and surrounding whitespace; account lookup itself is unchanged. A successful login
clears previous failures. Database/system errors release the reservation without
counting as incorrect credentials.

**Recovery:** wait for the temporary lockout to expire, then sign in with valid
credentials. Requests rejected during lockout do not extend it. This does not
clear `User.IsLocked`, the existing administrative lock flag; administering that
flag belongs to the user-administration work. No password-reset flow is implied.

All rejected logins return the same `401` detail, `Invalid credentials`, including
unknown users, wrong/empty passwords, administrative locks and temporary lockouts.
Unknown and administratively locked users perform dummy PBKDF2 work to reduce
timing differences. Usernames must be nonblank and at most 128 characters, and
passwords at most 1,024 characters; registration also rejects empty passwords with
400 before hashing. These limits use .NET string length, without truncation.

The failed-attempt store is bounded to **10,000 username keys**. At capacity it
reclaims expired, non-running records and rejects new keys if still full; it never
evicts a live lockout to accommodate another username. Both lockout and request
limits **reset on process restart and are not shared between instances**. Use a
shared atomic lockout store and shared ingress enforcement before multi-instance
deployment or when lockouts must survive restart. No database schema change is
needed for this single-process implementation.

## Token claims

Access tokens identify an account with:

- `nameid` (`ClaimTypes.NameIdentifier`) — the stable user ID; this is the
  subject. Never identify an account by display name.
- `name` (`ClaimTypes.Name`) — the username.
- `email` (`ClaimTypes.Email`) — the account's address.
- `OrganisationId` — the account's organisation, spelled as in the API's
  organisation models.

No role claim is issued. Roles must come from granted role assignments, which
nothing reads yet (checklist: role/permission evaluation), so authorization
today depends only on a valid identity.

## Logging and diagnostics

Every request runs inside a log scope (request path, hosting request id, and a
`RequestCorrelation` scope), so entries for one request group together; the
console formatter prints scopes (`Console:IncludeScopes`). Application services
log operation outcomes with structured state: login success or failure and
registration success or conflict carry the account name, the failure reason,
and elapsed milliseconds. Unhandled failures log method, path, and the same
trace id the client receives in the 500 problem body — the problem-details
writer computes it as `Activity.Current.Id` when an activity exists, falling
back to the request's trace identifier, so the handler logs that expression.

Passwords, hashes, tokens, and request bodies are never logged. The 500 body
stays opaque apart from its `traceId`; exception details exist only in the
server log.

## Operational signals

Request/error counts, framework latency histograms, authentication failures, and
terminal SQLite contention are collectable with .NET diagnostic tools. Requests
taking at least one second log their route template, elapsed time and trace ID.
See [the operator runbook](docs/OPERATIONS.md) for collection destinations,
commands, alert thresholds, performance profiling and the future delivery-signal
contract.

## Verification

```sh
dotnet run --project tests/WITS.Api.SmokeTests
```

This executable smoke check covers startup validation, error responses, DI, and database targeting with isolated
fixtures. See [its README](tests/WITS.Api.SmokeTests/README.md) for scope. Run it
explicitly; `dotnet test` does not execute it.
