# API startup smoke check

From the repository root:

```sh
dotnet run --project tests/WITS.Api.SmokeTests
```

This executable check starts the real API with in-memory SQLite, requests a protected route, and resolves the database factory, unit of work, repositories, and application services. It checks the configured connection string and unit-of-work scope isolation. Failed assertions or startup failures return a nonzero exit code.

Run it explicitly; it is not a `dotnet test` project. It does not apply migrations or verify registration/login behavior.
