using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Mappers;
using Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Services;

/// <summary>
/// Network-subsystem boundary for a remote agent. Registration is deliberately local-only;
/// a later SSH/HTTP transport implementation will perform authenticated communication through this boundary.
/// </summary>
public interface IRemoteAgentTransportDataAccessNetService
{
    RemoteAgentTransportRegistrationDataAccessNetModel RegisterEndpoint(RemoteAgentEndpointDataAccessNetModel endpoint);
}

public sealed class RemoteAgentTransportDataAccessNetService : IRemoteAgentTransportDataAccessNetService
{
    public RemoteAgentTransportRegistrationDataAccessNetModel RegisterEndpoint(RemoteAgentEndpointDataAccessNetModel endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (string.IsNullOrWhiteSpace(endpoint.AgentId))
        {
            throw new ArgumentException("Agent id is required.", nameof(endpoint));
        }

        ArgumentNullException.ThrowIfNull(endpoint.ControlEndpoint);
        if (!endpoint.ControlEndpoint.IsAbsoluteUri || endpoint.ControlEndpoint.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("Remote agent control endpoint must be an absolute HTTP(S) URI.", nameof(endpoint));
        }

        return RemoteAgentTransportDataAccessNetMapper.ToRegistration(endpoint, DateTimeOffset.UtcNow);
    }
}
