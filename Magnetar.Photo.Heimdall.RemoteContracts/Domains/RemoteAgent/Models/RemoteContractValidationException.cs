namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

public sealed class RemoteContractValidationException(string message) : ArgumentException(message);