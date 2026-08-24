namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

/// <summary>Transport kinds reserved for remote Heimdall agents. No network activity is implied by this model.</summary>
public enum RemoteAgentTransportDataAccessNetKind
{
    Https,
    SshTunnel
}

/// <summary>Validated endpoint configuration owned by the network sublayer.</summary>
public sealed record RemoteAgentEndpointDataAccessNetModel(
    string AgentId,
    Uri ControlEndpoint,
    RemoteAgentTransportDataAccessNetKind TransportKind);

/// <summary>Local registration result. This contains no credentials and performs no network traffic.</summary>
public sealed record RemoteAgentTransportRegistrationDataAccessNetModel(
    string AgentId,
    Uri ControlEndpoint,
    RemoteAgentTransportDataAccessNetKind TransportKind,
    DateTimeOffset RegisteredAt);
