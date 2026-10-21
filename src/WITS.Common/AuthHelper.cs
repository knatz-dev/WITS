// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace WITS.Common;

public static class AuthHelper
{
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
        using var hmac = new HMACSHA256();
        var salt = Convert.ToBase64String(hmac.Key);
        var hash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));
        return (hash, salt);
    }

    /// <summary>
    /// Verifies if two passwords match.
    /// </summary>
    /// <param name="password1"></param>
    /// <param name="password2"></param>
    /// <returns></returns>
    public static bool ArePasswordsEqual(string password1, string password2)
    {
        return !string.IsNullOrEmpty(password1) && !string.IsNullOrEmpty(password2)
            && password1.Trim().Equals(password2.Trim(), StringComparison.OrdinalIgnoreCase);
    }


    /// <summary>
    /// Verifies the password against the stored hash and salt.
    /// </summary>
    /// <param name="password"></param>
    /// <param name="storedHash"></param>
    /// <param name="storedSalt"></param>
    /// <returns></returns>
    public static bool VerifyPasswordHash(string password, string? storedHash, string storedSalt)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(storedSalt))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(storedSalt));
        var computedHash = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(password)));
        return computedHash == storedHash;
    }

}
