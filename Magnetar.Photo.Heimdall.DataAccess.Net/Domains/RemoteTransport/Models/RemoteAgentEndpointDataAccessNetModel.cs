namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

/// <summary>Validated endpoint configuration owned by the network sublayer.</summary>
public sealed record RemoteAgentEndpointDataAccessNetModel(
    string AgentId,
    Uri ControlEndpoint,
    RemoteAgentTransportDataAccessNetKind TransportKind);