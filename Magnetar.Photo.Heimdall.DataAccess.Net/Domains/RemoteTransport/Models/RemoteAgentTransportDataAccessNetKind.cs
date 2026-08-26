namespace Magnetar.Photo.Heimdall.DataAccess.Net.Domains.RemoteTransport.Models;

/// <summary>Transport kinds reserved for remote Heimdall agents. No network activity is implied by this model.</summary>
public enum RemoteAgentTransportDataAccessNetKind
{
    Https,
    SshTunnel
}