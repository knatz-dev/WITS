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

public record CredentialResult(bool Success, string? Token, string Message = "");
public record RegistrationResult(bool Success, string Message = "", bool Conflict = false);

public interface IAuthService
{
    string GenerateToken(UserTokenModel userToken);
    Task<CredentialResult> ValidateCredentialsAsync(string username, string password);
    Task<RegistrationResult> RegisterUserAsync(RegisterRequest user);
}

public class AuthService : IAuthService
{
    private readonly IGenericRepository<User, long> _userRepository;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IUnitOfWork unitOfWork, IOptions<JwtSettings> jwtSettings, ILogger<AuthService> logger)
    {
        _userRepository = unitOfWork.GetRepository<User, long>();
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
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
            _logger.LogWarning("Login failed for {Username}: unknown account after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, "User account not found!");
        }

        // Check if user is locked
        if (user.IsLocked)
        {
            _logger.LogWarning("Login failed for {Username}: account locked after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, "User account is locked. Please contact support.");
        }

        // If password is null or empty, return invalid credentials
        if (string.IsNullOrEmpty(password))
        {
            _logger.LogWarning("Login failed for {Username}: empty password after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, "Password cannot be empty");
        }

        // Check if password matches the stored hash and salt
        bool hashMatched = AuthHelper.VerifyPasswordHash(password, user.PasswordHash, user.PasswordSalt ?? string.Empty);

        // Use salt and hash for verification
        if (!hashMatched)
        {
            _logger.LogWarning("Login failed for {Username}: password mismatch after {ElapsedMs} ms",
                username, stopwatch.ElapsedMilliseconds);
            return new CredentialResult(false, null, "Invalid credentials");
        }

        // Generate token if credentials are valid
        var token = GenerateToken(new UserTokenModel()
        {
            Id = user.Id,
            Username = username,
            EmailAddress = user.EmailAddress,
            OrganisationId = user.OrganisationId,
        });

        _logger.LogInformation("Login succeeded for {Username} after {ElapsedMs} ms",
            username, stopwatch.ElapsedMilliseconds);
        return new CredentialResult(true, token);
    }
 
    public async Task<RegistrationResult> RegisterUserAsync(RegisterRequest user)
    {
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
