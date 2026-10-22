# API startup smoke check

From the repository root:

```sh
dotnet run --project tests/WITS.Api.SmokeTests
```

This executable check starts the real API with in-memory SQLite, requests a protected route, and resolves the database factory, unit of work, repositories, and application services. It checks the configured connection string and unit-of-work scope isolation. Failed assertions or startup failures return a nonzero exit code.

Startup-validation checks cover missing/blank issuer, audience and signing key, undersized keys, non-development sample keys, nonpositive/malformed token lifetimes, and invalid database configuration. Real-host checks reject read-only, missing (`Mode=ReadWrite`), corrupt, and unusable-directory database targets before any request is sent. A successful startup preserves a fixture's schema, data, and `user_version`. Boundary-length ASCII/UTF-8 keys issue tokens that validate against both the actual bearer settings and the expected UTF-8 signing key.

Repository checks use real SQLite to verify that `GetAllAsync` preserves empty-page metadata and returns active rows/counts, while connection-open, missing-table, and row-mapping failures propagate to callers. These checks target the repository directly; ticket-service HTTP integration is still unimplemented.

Error-response checks run the API in `Production` and require `application/problem+json` bodies carrying `status`, `title`, and `detail` for every expected failure: an unauthenticated request (keeping `WWW-Authenticate`), rejected credentials, duplicate registration (409), registration validation (400), a missing ticket (404), an unreadable request body, an authorization refusal, and an unhandled service exception. The unhandled case must also stay free of exception and stack-trace content. Ticket lookup and registration are exercised with stubbed services because the real services need a schema and working credentials.

Password-storage checks assert that a freshly hashed password round-trips, that a wrong or empty password and a missing salt fail, that each password gets its own salt, that a stored hash records a real work factor and verification derives with exactly that cost, and that fourteen damaged/legacy stored values (unstructured text, legacy HMAC pairs, bad cost fields, bad Base64, truncated payloads) fail closed instead of throwing.

Identity checks run real credential validation against a stubbed account and require the issued token to authenticate, resolve `name` to the username, carry `ClaimTypes.NameIdentifier` subject `42`, the account email, and an `OrganisationId` claim using organisation naming — with no role claim, no display-name value anywhere in the token, and no token for a wrong password.

Diagnostics checks run the real `AuthService` through HTTP against a capturing log provider and require successful and failed logins to log their outcome with elapsed time, the unhandled-failure log entry to carry the trace id the client received in its 500 body, and no captured entry to contain a password or an issued token.

Transaction-ownership checks run multiple operations, a commit (verified visible through a separate connection), a rollback, and repository reuse across transaction boundaries on a temporary SQLite database, and require the unit of work to be the sole connection owner with live transaction enlistment.

Operational-signal checks listen to real .NET metrics and assert request counts/status/latency, the six credential/bearer failure reasons, bounded route labels and no sensitive labels. A real SQLite busy timeout and simulated locked failure count as contention; another SQLite error does not. The real timeout must also log a slow request with its route template and client-visible trace ID. Authentication-abuse checks additionally require the three new abuse-control reasons. Collection commands and alert policy are in [the operator runbook](../../docs/OPERATIONS.md).

Authentication-abuse checks exercise the real service through HTTP with a stubbed account and an injected clock: five failures block even a correct password across different IPs, unknown accounts receive the same generic failure, timed recovery issues a token, success resets the count, administrative locks remain locked, and invalid credential fields are rejected. Real missing-schema failures must release the account reservation without consuming failure counts. Tracker checks cover concurrent admission, normalized keys, non-extending lockout, bounded capacity and expiry cleanup. Separate HTTP checks use a counting/blocking auth service to prove shared login/registration IP limits (including malformed input and mapped IPv6), ignored spoofed forwarding headers, aggregate limits across IPs, four-request concurrency with no queue, released permits, and 429 ProblemDetails/Retry-After including an unsupported Accept header. Clock-based checks avoid sleeping for lockout recovery; fixed-window replenishment is provided by the framework rather than reimplemented in tests.

It also runs the real migrator and seeder against temporary SQLite files and checks:

- Environment JSON, environment-variable overrides, and CLI overrides select the same file as the API.
- `DOTNET_ENVIRONMENT` takes precedence over `ASPNETCORE_ENVIRONMENT`; CLI `--environment` takes precedence over both in the tools.
- Tool settings come from the executable directory even with conflicting JSON in the working directory.
- Missing/relative paths are rejected; absolute paths containing spaces, equals signs, and semicolons work.
- Migration bootstrap, history inspection, and rollback use the explicit target, without touching a decoy database.
- Maintenance outcomes are reliable: the seeder reports a real seeding failure and exits nonzero with stdin held open (no console wait), migration removal exits nonzero and keeps the file when a deletion fails (`remove <id>` and `remove all`, using an exclusively-open file on Windows), and successful removal exits zero and deletes the file.

The seeder check verifies the file it opens; a separate check asserts its nonzero failure exit, since insertion itself still fails on the current schema. The migration-application check verifies history-table bootstrap on the selected file; the existing count-cast defect still blocks up-scripts. A synthetic history row exercises rollback independently. These known defects remain separate checklist items.

Run this explicitly; it is not a `dotnet test` project. Temporary databases and environment JSON fixtures are removed after the run. Real credential storage, password verification, ticket-service behavior, and production data/filesystem permissions are not exercised.
