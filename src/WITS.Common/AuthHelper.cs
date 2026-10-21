// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;

namespace WITS.Common;

public static class AuthHelper
{
    // Scheme and cost travel with the stored value, so iterations can be raised later without breaking existing rows.
    private const string Scheme = "PBKDF2";
    private const int Iterations = 600_000;
    private const int MaxIterations = 5_000_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    /// <summary>
    /// Generates a hash and salt for the given password.
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    public static (string hash, string salt) HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return ($"{Scheme}${Iterations}${Convert.ToBase64String(hash)}", Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Verifies the password against the stored hash and salt.
    /// </summary>
    /// <param name="password"></param>
    /// <param name="storedHash"></param>
    /// <param name="storedSalt"></param>
    /// <returns></returns>
    public static bool VerifyPasswordHash(string password, string? storedHash, string? storedSalt)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        var parts = storedHash.Split('$');
        if (parts.Length != 3 || parts[0] != Scheme
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations)
            || iterations is < 1 or > MaxIterations)
        {
            // Legacy HMAC values and damaged rows never verify; those accounts need a password reset.
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(parts[2]);
            if (expected.Length == 0)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, Convert.FromBase64String(storedSalt), iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
