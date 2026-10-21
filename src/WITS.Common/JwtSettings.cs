namespace WITS.Common;

public class JwtSettings
{
    public required string SecurityKey { get; init; }
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public int ExpirationMinutes { get; init; }
}
