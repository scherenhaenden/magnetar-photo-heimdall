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