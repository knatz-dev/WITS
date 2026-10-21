namespace WITS.Api.Common;

public interface IEndpointDefinition
{
    void Register(RouteGroupBuilder routeBuilder);
}
