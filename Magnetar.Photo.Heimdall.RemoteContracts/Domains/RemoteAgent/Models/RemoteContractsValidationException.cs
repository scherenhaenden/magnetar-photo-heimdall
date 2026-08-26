namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

public sealed class RemoteContractsValidationException(string message) : ArgumentException(message);