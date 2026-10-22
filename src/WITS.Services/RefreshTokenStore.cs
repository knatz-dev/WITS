namespace WITS.Services;

using System.Security.Cryptography;

// shortcut: refresh tokens are process-local and reset on restart; persist and share them before multi-instance deployment.
public sealed class RefreshTokenStore(TimeProvider clock)
{
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(7);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _tokens = new(StringComparer.Ordinal);
    private readonly Dictionary<long, HashSet<string>> _byUser = new();

    public string Issue(long userId)
    {
        var token = GenerateToken();
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            _tokens[token] = new Entry
            {
                Token = token,
                UserId = userId,
                FamilyId = Guid.NewGuid(),
                CreatedAt = now,
                ExpiresAt = now + RefreshLifetime
            };
            if (!_byUser.TryGetValue(userId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _byUser[userId] = set;
            }
            set.Add(token);
        }
        return token;
    }

    // Returns (userId, newRefreshToken, reused). reused means a rotated token was presented again.
    public (long? UserId, string? NewToken, bool Reused) Rotate(string presented)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(presented, out var entry)) return (null, null, false);
            var now = clock.GetUtcNow();
            if (entry.ExpiresAt <= now)
            {
                _tokens.Remove(presented);
                _byUser.GetValueOrDefault(entry.UserId)?.Remove(presented);
                return (null, null, false);
            }
            // Reuse detection must precede the generic revoked check: a rotated token is
            // marked both consumed and revoked, and presenting it again revokes the family.
            if (entry.Consumed)
            {
                RevokeFamily(entry.FamilyId);
                return (null, null, true);
            }
            if (entry.Revoked)
            {
                return (null, null, false);
            }

            entry.Consumed = true;
            entry.Revoked = true;
            entry.RevokedAt = now;

            var newToken = GenerateToken();
            var newEntry = new Entry
            {
                Token = newToken,
                UserId = entry.UserId,
                FamilyId = entry.FamilyId,
                CreatedAt = now,
                ExpiresAt = now + RefreshLifetime
            };
            _tokens[newToken] = newEntry;
            if (!_byUser.TryGetValue(entry.UserId, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _byUser[entry.UserId] = set;
            }
            set.Add(newToken);
            entry.ReplacedBy = newToken;
            return (entry.UserId, newToken, false);
        }
    }

    public bool Revoke(string token)
    {
        lock (_gate)
        {
            if (!_tokens.TryGetValue(token, out var entry)) return false;
            if (!entry.Revoked)
            {
                entry.Revoked = true;
                entry.RevokedAt = clock.GetUtcNow();
            }
            return true;
        }
    }

    public void RevokeAll(long userId)
    {
        lock (_gate)
        {
            if (!_byUser.TryGetValue(userId, out var set)) return;
            foreach (var token in set.ToArray())
            {
                if (_tokens.TryGetValue(token, out var entry))
                {
                    entry.Revoked = true;
                    entry.RevokedAt = clock.GetUtcNow();
                }
            }
            set.Clear();
        }
    }

    private void RevokeFamily(Guid familyId)
    {
        foreach (var entry in _tokens.Values.Where(e => e.FamilyId == familyId))
        {
            entry.Revoked = true;
            entry.Consumed = true;
            entry.RevokedAt = clock.GetUtcNow();
        }
    }

    private static string GenerateToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class Entry
    {
        public required string Token { get; init; }
        public long UserId { get; init; }
        public Guid FamilyId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
        public bool Consumed { get; set; }
        public bool Revoked { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? ReplacedBy { get; set; }
    }
}
