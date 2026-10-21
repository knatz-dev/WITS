using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WITS.Common;

namespace WITS.Api.SmokeTests;

internal static class PasswordStorageChecks
{
    private const string Password = "correct horse battery staple";

    public static void Run()
    {
        var (hash, salt) = AuthHelper.HashPassword(Password);
        Require(hash.StartsWith("PBKDF2$", StringComparison.Ordinal), "Stored hashes must identify their KDF scheme.");
        Require(AuthHelper.VerifyPasswordHash(Password, hash, salt), "A freshly stored password must verify.");
        Require(!AuthHelper.VerifyPasswordHash($"{Password}!", hash, salt), "A wrong password must fail verification.");
        Require(!AuthHelper.VerifyPasswordHash(string.Empty, hash, salt), "An empty password must fail verification.");
        Require(!AuthHelper.VerifyPasswordHash(Password, hash, null), "A missing salt must fail closed.");

        var repeat = AuthHelper.HashPassword(Password);
        Require(repeat.salt != salt && repeat.hash != hash, "Every stored password needs its own salt and hash.");

        var parts = hash.Split('$');
        var recordedCost = int.Parse(parts[1], CultureInfo.InvariantCulture);
        Require(recordedCost >= 100_000, "Password derivation must record a real work factor.");
        var otherCost = $"{parts[0]}${recordedCost + 1}${parts[2]}";
        Require(!AuthHelper.VerifyPasswordHash(Password, otherCost, salt),
            "Verification must derive with the cost recorded in the stored value.");

        try
        {
            _ = AuthHelper.HashPassword(string.Empty);
            throw new InvalidOperationException("Hashing an empty password must be rejected.");
        }
        catch (ArgumentException)
        {
            // Registration must not store an empty password.
        }

        using var legacy = new HMACSHA256();
        var damaged = new (string Label, string? StoredHash, string? StoredSalt)[]
        {
            ("null hash", null, salt),
            ("empty hash", string.Empty, salt),
            ("null salt", hash, null),
            ("empty salt", hash, string.Empty),
            ("unstructured value", "adminhashedpassword", salt),
            ("legacy HMAC hash and key", Convert.ToBase64String(legacy.ComputeHash(Encoding.UTF8.GetBytes(Password))), Convert.ToBase64String(legacy.Key)),
            ("non-numeric cost", "PBKDF2$many$AQID", salt),
            ("zero cost", "PBKDF2$0$AQID", salt),
            ("excessive cost", "PBKDF2$2000000000$AQID", salt),
            ("missing cost part", "PBKDF2$AQID", salt),
            ("empty hash payload", "PBKDF2$600000$", salt),
            ("hash payload not base64", "PBKDF2$600000$!!!", salt),
            ("salt not base64", hash, "not-base64!"),
            ("truncated hash payload", "PBKDF2$600000$AQID", salt)
        };

        foreach (var stored in damaged)
        {
            Require(!AuthHelper.VerifyPasswordHash(Password, stored.StoredHash, stored.StoredSalt),
                $"Damaged stored credentials must fail closed instead of throwing or matching: {stored.Label}.");
        }

        Console.WriteLine("PASS: PBKDF2 password storage round-trips, rejects wrong passwords, and fails closed on damaged values.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
