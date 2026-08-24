using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Mappers;

internal static class RemoteAgentTransportDataAccessNetMapper
{
    public static RemoteAgentTransportRegistrationDataAccessNetModel ToRegistration(RemoteAgentEndpointDataAccessNetModel endpoint, DateTimeOffset registeredAt) =>
        new(endpoint.AgentId, endpoint.ControlEndpoint, endpoint.TransportKind, registeredAt);
}
