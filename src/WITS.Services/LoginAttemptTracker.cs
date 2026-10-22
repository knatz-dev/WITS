namespace WITS.Services;

// shortcut: state is process-local and resets on restart; use a shared atomic store before running multiple API instances.
public sealed class LoginAttemptTracker(TimeProvider clock)
{
    public const int FailureLimit = 5;
    public static readonly TimeSpan Lockout = TimeSpan.FromMinutes(15);
    private const int Capacity = 10_000;
    private readonly object _gate = new();
    private readonly Dictionary<string, Attempt> _attempts = new(StringComparer.OrdinalIgnoreCase);

    public bool TryBegin(string username)
    {
        lock (_gate)
        {
            var key = username.Trim();
            var now = clock.GetUtcNow();
            if (_attempts.TryGetValue(key, out var attempt))
            {
                // Serialize attempts for an account so concurrent failures cannot bypass the threshold.
                if (attempt.InProgress) return false;
                if (attempt.ExpiresAt <= now)
                {
                    _attempts.Remove(key);
                    attempt = null;
                }
                else if (attempt.Failures >= FailureLimit) return false;
            }

            if (attempt is null)
            {
                if (_attempts.Count >= Capacity)
                {
                    foreach (var expired in _attempts.Where(item => !item.Value.InProgress && item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
                    {
                        _attempts.Remove(expired);
                    }
                    // Never evict a live lockout to admit another attacker-controlled username.
                    if (_attempts.Count >= Capacity) return false;
                }
                attempt = new Attempt { ExpiresAt = now + Lockout };
                _attempts.Add(key, attempt);
            }

            attempt.InProgress = true;
            return true;
        }
    }

    // Null means infrastructure failure: release the reservation without penalizing credentials.
    public void Complete(string username, bool? success)
    {
        lock (_gate)
        {
            var key = username.Trim();
            var attempt = _attempts[key];
            attempt.InProgress = false;
            if (success == true || (success is null && attempt.Failures == 0))
            {
                _attempts.Remove(key);
            }
            else if (success == false)
            {
                attempt.Failures++;
                if (attempt.Failures >= FailureLimit) attempt.ExpiresAt = clock.GetUtcNow() + Lockout;
            }
        }
    }

    private sealed class Attempt
    {
        public int Failures { get; set; }
        public bool InProgress { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
