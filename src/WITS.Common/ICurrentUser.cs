namespace WITS.Common;

public interface ICurrentUser
{
    long? UserId { get; }
    int? OrganisationId { get; }
    bool IsAuthenticated { get; }
}