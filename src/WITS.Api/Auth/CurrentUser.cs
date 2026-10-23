using System.Security.Claims;

namespace WITS.Api.Auth;

public interface ICurrentUser
{
    long? UserId { get; }
    int? OrganisationId { get; }
    bool IsAuthenticated { get; }
}

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public long? UserId
    {
        get
        {
            var value = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(value, out var id) ? id : null;
        }
    }

    public int? OrganisationId
    {
        get
        {
            var value = Principal?.FindFirst("OrganisationId")?.Value;
            return int.TryParse(value, out var id) ? id : null;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
}
