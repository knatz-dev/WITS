# Operational signals

Database backup and restore procedure: [BACKUP.md](BACKUP.md).

## Collection destination

The initial destination is **operator-owned files on the API host**: metrics as
JSON from `dotnet-counters`, application stdout/stderr as a log file, and short
`.nettrace` captures for performance investigations. This uses .NET's existing
diagnostic transport; no public metrics endpoint or application exporter is needed.
Run collection as the API's OS account. Each deployment must supervise collection
alongside the API and restart it with the new process ID after an API restart.
Measurements before attachment are not retained. Missing samples mean **unknown**,
not healthy or zero traffic.

PowerShell example (repository root, with database/JWT environment settings from
the root README already supplied). Select a directory per environment and instance:

```powershell
# One-time operator tools; these are not application dependencies.
dotnet tool install --global dotnet-counters --version '9.*'
dotnet tool install --global dotnet-trace --version '9.*'

$destination = 'C:\WITSData\Diagnostics\Development\api-1'
New-Item -ItemType Directory -Force $destination | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
dotnet run --project src/WITS.Api --no-build --no-launch-profile -- --urls http://localhost:5351 *> "$destination\api-$stamp.log"
```

In another terminal, select the **WITS.Api process**, not the `dotnet run` parent:

```powershell
dotnet-counters ps
$apiProcessId = 1234 # replace with the WITS.Api PID
$destination = 'C:\WITSData\Diagnostics\Development\api-1'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
dotnet-counters collect --process-id $apiProcessId --counters WITS,Microsoft.AspNetCore.Hosting,System.Runtime --refresh-interval 5 --maxHistograms 256 --maxTimeSeries 1024 --format json --output "$destination\metrics-$stamp.json"
```

Stop collection with Q or Ctrl+C to finalize JSON; for a bounded capture add
`--duration 00:05:00`. The elevated histogram limit matters: the tool's default of
10 drops route/status series on even this small API. Treat any tracking-limit
warning as incomplete data. For live viewing use `monitor` instead of `collect`
and omit `--format`/`--output`. Only one collection session is needed per instance.

Keep rolling log/metric captures for 7 days and incident profiles for 24 hours
unless attached to an investigation. Configure file rotation/retention in the
deployment supervisor; the application does not manage these files. Restrict the
directory to operators: existing diagnostic logs contain account names and paths,
and profiles may contain application data. Central shipping and automated paging
are deployment integrations; this repository defines the signals and thresholds
but does not provision an alerting service.

## Signal catalogue

| Signal (meter) | Meaning and dimensions |
| --- | --- |
| `wits.http.requests` (`WITS`) | Counter, requests leaving the API pipeline, tagged by `http.route` **template** and `http.response.status_code`. Includes handled 4xx/5xx. Unmatched paths share `(unmatched)`. Use status totals for traffic, client-error, and server-error rates. This records the response status, not a guarantee that the client received the entire body. |
| `http.server.request.duration` (`Microsoft.AspNetCore.Hosting`) | Framework histogram in **seconds**, tagged by route template, method, status and other framework HTTP dimensions. Includes authentication, service/database work and response processing. The tool exports per-interval p50/p95/p99; it does not export histogram counts, hence the separate request counter. |
| `http.server.active_requests` (`Microsoft.AspNetCore.Hosting`) | In-flight requests; sustained growth alongside latency suggests saturation. |
| `wits.auth.failures` (`WITS`) | Counter, one per rejected credential check, bearer challenge or abuse-control rejection. Fixed `reason`: `unknown_account`, `account_locked`, `empty_password`, `password_mismatch`, `missing_bearer`, `invalid_bearer`, `invalid_input`, `login_throttled`, `rate_limited`. `login_throttled` covers temporary account lockout, an already-running account check, or a full attempt store; it produces the same generic 401. `rate_limited` covers HTTP IP/process/concurrency limits and produces 429. `invalid_input` covers invalid credential field lengths/nulls. A rejected login is not counted again as a bearer challenge. Successful login, 403, malformed JSON and database failures do not increment this counter (a malformed request rejected by the limiter still counts as `rate_limited`). |
| `wits.sqlite.contention` (`WITS`) | Counter of **terminal** SQLite exceptions reaching the API exception handler: base code 5 → `error.type=busy`, 6 → `locked` (including extended codes with those base codes). Other database errors remain 500s, not contention. It does not count internal driver retries, waits that eventually succeed, maintenance tools, or future workers. Successful long waits still affect request latency. |
| `System.Runtime` | Runtime CPU, GC, memory and thread-pool measurements for distinguishing expensive computation from blocked work. |

Counters exported by `dotnet-counters` 9 are interval deltas (labelled
`{unit} / N sec`): divide by the refresh interval to get a per-second rate. Sum
request deltas over the window before calculating error fractions. Percentiles
cannot be averaged or summed across intervals, routes or instances; use them to
inspect each series. A future metrics backend should aggregate histogram buckets
for fleet-wide percentiles. Do not derive fleet percentiles from these JSON values.

Custom metric labels never include user/organisation IDs, usernames, raw URLs,
query strings, SQL, passwords, tokens, or exception messages. Trace IDs belong in
logs rather than metric dimensions. ASP.NET Core supplies the request Activity;
the existing error log and new slow-request log use the same Activity ID as a 500
problem body's `traceId` (falling back to the hosting request identifier).

## Initial alert policy

Apply these per environment/instance, then tune against normal traffic. The
deployment operator owns the alerts and hands application faults to the API owner.
These are starting thresholds, not promised service-level objectives.

| Condition | Action |
| --- | --- |
| 5xx / all requests > 1% over 5 minutes, with at least 100 requests | Page API owner. With lower traffic, any 5xx is an investigation warning instead of a percentage-based page. Inspect trace-correlated error logs. Keep 4xx separate. |
| Route/status p95 > 1 second in three consecutive 5-second samples, with at least 20 requests in each sample | Warn API owner. Inspect slow-request logs and runtime measurements; profile the affected instance. Do not merge per-series percentiles. |
| Any terminal SQLite contention in 5 minutes | Warn; page if at least 5 failures in that window. Look for concurrent writers, long transactions and overlapping migrator/seeder jobs. Do not interpret contention as permission to increase timeouts blindly. |
| At least 20 credential or invalid-bearer failures per minute for 5 minutes | Warn operator to investigate authentication logs and traffic. Exclude `missing_bearer` from this rule; track it separately because anonymous probes are common. |
| Sustained `login_throttled` or `rate_limited` increments for 5 minutes | Inspect source traffic, shared-proxy buckets and affected accounts. Temporary locks expire 15 minutes after the fifth failure; rejected retries do not extend them. Do not clear administrative `IsLocked` flags as an abuse-control recovery step. |
| Collector/process exits or no metric samples for two collection intervals while API is expected to run | Alert operator about a collection/availability gap. Do not infer recovery from absent metrics. Startup failures are also in process exit status/stderr. |

## Investigating a slow operation

Every request taking **at least 1 second** through the application middleware
emits a `Slow request` warning with method, route template, response status,
elapsed milliseconds and trace ID. This remains enabled at the default log levels.
Find that trace ID in the application logs to connect auth outcome or exception
details to the slow request. For a reported 500, start with the response's `traceId`.

If runtime metrics indicate CPU pressure, collect a short profile while reproducing:

```powershell
dotnet-trace collect --process-id $apiProcessId --profile dotnet-sampled-thread-time --duration 00:00:30 --output "$destination\slow-$stamp.nettrace"
```

Open the capture in PerfView or Visual Studio's profiler and examine hot stacks
in the affected time window. This profile samples managed thread stacks to estimate
wall-clock time, not precise CPU usage or stored distributed spans; correlate its
time window with the log. If latency is high without CPU
pressure, inspect contention, active requests and transaction duration first.

See [authentication abuse controls](../README.md#authentication-abuse-controls)
for the exact request limits, automatic recovery, proxy assumptions and
process-local enforcement boundary.

## Delivery signals (contract for the future worker)

There is currently no delivery worker. The following signals are **not emitted**;
their absence is not evidence of successful delivery. Add and test them with the
outbox/notification implementation:

- `wits.delivery.attempts`: counter tagged by fixed `channel` (`email`/`webhook`)
  and `outcome` (`success`/`retry`/`permanent_failure`). Count each attempt once,
  after its persisted outcome; never tag recipient, URL, payload, or work-item ID.
- `wits.delivery.pending`: observable gauge of persisted pending items per channel.
- `wits.delivery.oldest_pending_age`: observable gauge in seconds per channel,
  computed from persisted UTC enqueue time, zero only when the queue is empty.
- Warn on any permanent failure; page the delivery owner if oldest pending age
  exceeds 300 seconds for 5 minutes. Require worker/collector liveness separately
  so a dead worker cannot look like an empty queue. Send these through the same
  instance-specific collection destination when the worker exists.

## Verification

`dotnet run --project tests/WITS.Api.SmokeTests` checks actual emitted measurements
using `MeterListener`: completed requests and handled errors, latency, all six auth
failure reasons without double counting, unmatched-path grouping, label privacy,
a real SQLite busy timeout, a simulated locked failure, and exclusion of other
SQLite errors. It also requires the real wait to produce a slow log with the
client-visible trace ID. Fixtures replace ticket service behavior and account
storage; production migrations/ticket commands are not needed for these checks.

Collection can be checked end-to-end without a deployed API:

```powershell
dotnet-counters collect --format json --refresh-interval 1 --maxHistograms 256 --counters WITS,Microsoft.AspNetCore.Hosting --output "$destination\smoke-metrics.json" -- dotnet tests/WITS.Api.SmokeTests/bin/Debug/net9.0/WITS.Api.SmokeTests.dll
```

The output should contain positive request/auth/SQLite counters and latency
percentiles. Short-lived test hosts can close before a histogram export interval;
verify slow-request percentiles on a long-running API instance. This command
launches the compiled smoke executable; build it first. The tool redirects its
child's console output, so run the executable directly as well for its assertion
results. Collection commands were exercised with tool version `9.0.661903`.
