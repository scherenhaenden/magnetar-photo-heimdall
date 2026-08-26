namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

/// <summary>Local registration result. This contains no credentials and performs no network traffic.</summary>
public sealed record RemoteAgentTransportRegistrationDataAccessNetModel(
    string AgentId,
    Uri ControlEndpoint,
    RemoteAgentTransportDataAccessNetKind TransportKind,
    DateTimeOffset RegisteredAt);