using System.Diagnostics.Metrics;

namespace WITS.Common;

public static class OperationalSignals
{
    public const string MeterName = "WITS";
    private static readonly Meter Meter = new(MeterName);

    // EventPipe exports histogram percentiles but not counts; this supplies request/error rates to dotnet-counters.
    public static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "wits.http.requests", "{request}", "Requests leaving the API pipeline, by route template and response status.");

    public static readonly Counter<long> AuthenticationFailures = Meter.CreateCounter<long>(
        "wits.auth.failures", "{failure}", "Rejected credentials and bearer challenges, by fixed reason.");

    // shortcut: API-terminal contention only; instrument the data layer when retry/worker paths are introduced.
    public static readonly Counter<long> SqliteContention = Meter.CreateCounter<long>(
        "wits.sqlite.contention", "{failure}", "Terminal SQLITE_BUSY/SQLITE_LOCKED failures reaching the API exception handler.");
}
