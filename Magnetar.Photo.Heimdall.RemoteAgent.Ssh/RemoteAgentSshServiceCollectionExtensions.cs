using Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh;

public static class RemoteAgentSshServiceCollectionExtensions
{
    /// <summary>Registers the pinned SSH adapter without persisting credentials or host pins in the catalog.</summary>
    public static IServiceCollection AddHeimdallRemoteAgentSsh(
        this IServiceCollection services,
        SshRemoteAgentOptions options,
        Func<string, string?>? passphraseProvider = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        services.AddSingleton<IRemoteAgentRpcV1RemoteContractsContract>(
            _ => new SshRemoteAgentRpc(options, passphraseProvider, requestTimeout));
        return services;
    }
}
