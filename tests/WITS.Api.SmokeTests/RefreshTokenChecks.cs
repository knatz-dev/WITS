using System;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WITS.Common;
using WITS.Data;
using WITS.Data.Common;
using WITS.Data.Entity;
using WITS.Services;

namespace WITS.Api.SmokeTests;

internal static class RefreshTokenChecks
{
    private const string Password = "refresh-check-password";

    public static async Task RunAsync()
    {
        var settings = new JwtSettings
        {
            SecurityKey = StartupConfigurationChecks.SigningKey,
            Issuer = "smoke-issuer",
            Audience = "smoke-audience",
            ExpirationMinutes = 5
        };
        var (hash, salt) = AuthHelper.HashPassword(Password);
        var account = new User
        {
            Id = 42, Username = "refresh.user", FirstName = "Refresh", LastName = "User",
            EmailAddress = "refresh@example.com", OrganisationId = 7, PasswordHash = hash, PasswordSalt = salt
        };
        var clock = new TestClock();
        var store = new RefreshTokenStore(clock);
        var service = new AuthService(new IdentityClaimsChecks.StubUnitOfWork(account), Options.Create(settings),
            NullLogger<AuthService>.Instance, new LoginAttemptTracker(clock), store);

        var login = await service.ValidateCredentialsAsync(account.Username, Password);
        Require(login.Success && !string.IsNullOrEmpty(login.Token) && !string.IsNullOrEmpty(login.RefreshToken),
            "Login must issue access and refresh tokens.");

        var firstRefresh = await service.RefreshAsync(login.RefreshToken!);
        Require(firstRefresh.Success && !string.IsNullOrEmpty(firstRefresh.AccessToken) && !string.IsNullOrEmpty(firstRefresh.RefreshToken)
            && firstRefresh.RefreshToken != login.RefreshToken,
            "Refresh must rotate the refresh token.");

        var reused = await service.RefreshAsync(login.RefreshToken!);
        Require(!reused.Success, "A rotated refresh token must be rejected on reuse.");
        var afterReuse = await service.RefreshAsync(firstRefresh.RefreshToken!);
        Require(!afterReuse.Success, "Reuse must revoke the entire token family.");

        // Expiry
        var freshLogin = await service.ValidateCredentialsAsync(account.Username, Password);
        clock.Advance(RefreshTokenStore.RefreshLifetime);
        var expired = await service.RefreshAsync(freshLogin.RefreshToken!);
        Require(!expired.Success, "An expired refresh token must be rejected.");

        // Revocation
        var revokeLogin = await service.ValidateCredentialsAsync(account.Username, Password);
        service.RevokeRefreshToken(revokeLogin.RefreshToken!);
        var revoked = await service.RefreshAsync(revokeLogin.RefreshToken!);
        Require(!revoked.Success, "A revoked refresh token must be rejected.");

        // Account lock
        var lockLogin = await service.ValidateCredentialsAsync(account.Username, Password);
        account.IsLocked = true;
        var lockedRefresh = await service.RefreshAsync(lockLogin.RefreshToken!);
        Require(!lockedRefresh.Success, "Refresh must fail for a locked account.");
        account.IsLocked = false;

        // Revoke all
        var a = await service.ValidateCredentialsAsync(account.Username, Password);
        var b = await service.ValidateCredentialsAsync(account.Username, Password);
        service.RevokeAllRefreshTokens(account.Id);
        Require(!(await service.RefreshAsync(a.RefreshToken!)).Success && !(await service.RefreshAsync(b.RefreshToken!)).Success,
            "RevokeAll must invalidate every outstanding refresh token.");

        Console.WriteLine("PASS: refresh issues, rotates, detects reuse, enforces expiry, and supports revocation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}