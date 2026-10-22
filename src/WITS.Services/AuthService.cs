// ----------------------------------------------------------------------------------
//  Created by: Nathan Zwelibanzi Khupe
//  � 2025 Nathan Zwelibanzi Khupe. All rights reserved.
//  Unauthorized copying, distribution, modification, or use strictly prohibited.
//  ----------------------------------------------------------------------------------

using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using WITS.Common;
using WITS.Data;
using WITS.Data.Common;
using WITS.Data.Contracts;
using WITS.Data.Entity;
using WITS.Models;
using static WITS.Services.IAuthService;

namespace WITS.Services;

public record CredentialResult(bool Success, string? Token, string? RefreshToken = null, string Message = "");
public record RegistrationResult(bool Success, string Message = "", bool Conflict = false);
public record RefreshResult(bool Success, string? AccessToken, string? RefreshToken, string Message = "");

public interface IAuthService
{
    string GenerateToken(UserTokenModel userToken);
    Task<CredentialResult> ValidateCredentialsAsync(string username, string password);
    Task<RegistrationResult> RegisterUserAsync(RegisterRequest user);
    Task<RefreshResult> RefreshAsync(string refreshToken);
    void RevokeRefreshToken(string refreshToken);
    void RevokeAllRefreshTokens(long userId);
}

public class AuthService : IAuthService
{
    private const string InvalidCredentials = "Invalid credentials";
    private static readonly (string Hash, string Salt) DummyPassword =
        AuthHelper.HashPassword(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    private readonly IGenericRepository<User, long> _userRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;
    private readonly LoginAttemptTracker _loginAttempts;
    private readonly RefreshTokenStore _refreshTokens;

    public AuthService(IUnitOfWork unitOfWork, IOptions<JwtSettings> jwtSettings, ILogger<AuthService> logger, LoginAttemptTracker loginAttempts, RefreshTokenStore refreshTokens)
    {
        _userRepository = unitOfWork.GetRepository<User, long>();
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _loginAttempts = loginAttempts;
        _refreshTokens = refreshTokens;
    }

    public string GenerateToken(UserTokenModel userToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.SecurityKey);

        // Roles are deliberately absent: they must come from granted role assignments, never from display names.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userToken.Id.ToString()),
            new(ClaimTypes.Name, userToken.Username),
            new(ClaimTypes.Email, userToken.EmailAddress),
            new("OrganisationId", userToken.OrganisationId.ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public async Task<CredentialResult> ValidateCredentialsAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 128 || password is null || password.Length > 1024)
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "invalid_input"));
            return new CredentialResult(false, null, InvalidCredentials);
        }
        if (!_loginAttempts.TryBegin(username))
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "login_throttled"));
            _logger.LogWarning("Login throttled for {Username}", username);
            return new CredentialResult(false, null, InvalidCredentials);
        }

        bool? success = null;
        try
        {
            var result = await ValidateCredentialsCoreAsync(username, password);
            success = result.Success;
            return result;
        }
        finally
        {
            _loginAttempts.Complete(username, success);
        }
    }

    private async Task<CredentialResult> ValidateCredentialsCoreAsync(string username, string password)
    {
        var stopwatch = Stopwatch.StartNew();
        var filter = new GenericFilter
        {
            PageNumber = 1,
            PageSize = 1,
            Filters = new Dictionary<string, FilterCriteria>
            {
                { "Username", new FilterCriteria(username) }
            }
        };

        var result = await _userRepository.QueryAsync(filter);
        var user = result.Items.FirstOrDefault();

        if (user == null)
        {
            // Unknown and administratively locked accounts still perform password work to reduce account-enumeration timing differences.
            _ = AuthHelper.VerifyPasswordHash(password, DummyPassword.Hash, DummyPassword.Salt);
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "unknown_account"));
            _logger.LogWarning("Login failed for {Username}: unknown account after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, InvalidCredentials);
        }

        // Check if user is locked
        if (user.IsLocked)
        {
            _ = AuthHelper.VerifyPasswordHash(password, DummyPassword.Hash, DummyPassword.Salt);
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "account_locked"));
            _logger.LogWarning("Login failed for {Username}: account locked after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, InvalidCredentials);
        }

        // If password is null or empty, return invalid credentials
        if (string.IsNullOrEmpty(password))
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "empty_password"));
            _logger.LogWarning("Login failed for {Username}: empty password after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, InvalidCredentials);
        }

        // Check if password matches the stored hash and salt
        bool hashMatched = AuthHelper.VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt ?? string.Empty);

        // Use salt and hash for verification
        if (!hashMatched)
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "password_mismatch"));
            _logger.LogWarning("Login failed for {Username}: password mismatch after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, InvalidCredentials);
        }

        // Generate token if credentials are valid
        var token = GenerateToken(new UserTokenModel()
        {
            Id = user.Id,
            Username = username,
            EmailAddress = user.EmailAddress,
            OrganisationId = user.OrganisationId,
        });
        var refreshToken = _refreshTokens.Issue(user.Id);

        _logger.LogInformation("Login succeeded for {Username} after {ElapsedMs} ms",
            username, stopwatch.ElapsedMilliseconds);
        return new CredentialResult(true, token, refreshToken);
    }
 
    public Task<RefreshResult> RefreshAsync(string refreshToken)
    {
        var (userId, newRefresh, reused) = _refreshTokens.Rotate(refreshToken);
        if (reused)
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "refresh_reuse"));
            _logger.LogWarning("Refresh token reuse detected; revoked the affected token family");
            return Task.FromResult(new RefreshResult(false, null, null, "Invalid refresh token"));
        }
        if (userId is null || newRefresh is null)
        {
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "refresh_invalid"));
            return Task.FromResult(new RefreshResult(false, null, null, "Invalid refresh token"));
        }

        return CompleteRefreshAsync(userId.Value, newRefresh);
    }

    private async Task<RefreshResult> CompleteRefreshAsync(long userId, string newRefresh)
    {
        User? user;
        try { user = await _userRepository.GetByIdAsync(userId); }
        catch { user = null; }
        if (user is null || user.IsLocked)
        {
            _refreshTokens.RevokeAll(userId);
            OperationalSignals.AuthenticationFailures.Add(1, new KeyValuePair<string, object?>("reason", "account_locked"));
            _logger.LogWarning("Refresh rejected for user {UserId}: account unavailable or locked", userId);
            return new RefreshResult(false, null, null, "Invalid refresh token");
        }

        var accessToken = GenerateToken(new UserTokenModel
        {
            Id = user.Id,
            Username = user.Username,
            EmailAddress = user.EmailAddress,
            OrganisationId = user.OrganisationId
        });
        _logger.LogInformation("Refresh succeeded for {Username}", user.Username);
        return new RefreshResult(true, accessToken, newRefresh);
    }

    public void RevokeRefreshToken(string refreshToken) => _refreshTokens.Revoke(refreshToken);

    public void RevokeAllRefreshTokens(long userId) => _refreshTokens.RevokeAll(userId);

    public async Task<RegistrationResult> RegisterUserAsync(RegisterRequest user)
    {
        if (string.IsNullOrWhiteSpace(user.Username) || user.Username.Length > 128
            || string.IsNullOrEmpty(user.Password) || user.Password.Length > 1024)
        {
            return new RegistrationResult(false, "Username must be 1–128 characters and password must be 1–1024 characters.");
        }
        // Check if user exists
        var filter = new GenericFilter
        {
            PageNumber = 1,
            PageSize = 1,
            Filters = new Dictionary<string, FilterCriteria>
            {
                { "UserName", new FilterCriteria(user.Username)}
            }
        };

        var stopwatch = Stopwatch.StartNew();
        var existing = await _userRepository.QueryAsync(filter);
        if (existing.Items.Any())
        {
            _logger.LogWarning("Registration rejected for {Username}: username already exists after {ElapsedMs} ms",
                user.Username, stopwatch.ElapsedMilliseconds);
            return new RegistrationResult(false, "Username already exists", Conflict: true);
        }

        // Hash password with salt
        var (hash, salt) = AuthHelper.HashPassword(user.Password);
        user.Password = hash;
        user.Created = DateTime.UtcNow;

        int id = await _userRepository.InsertAsync(new User()
        {
            Id = 0,
            Username = user.Username,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmailAddress = user.EmailAddress,
            PasswordHash = hash,
            PasswordSalt = salt,
            Created = DateTime.UtcNow,
            OrganisationId = user.OrganisationId,
            PhoneNumber = user.TelephoneNos
        });
        _logger.LogInformation("Registered user {Username} with id {UserId} after {ElapsedMs} ms",
            user.Username, id, stopwatch.ElapsedMilliseconds);
        return new RegistrationResult(true, $"User registered successfully with id {id}.");
    }

}
