using Magnetar.Photo.Heimdall.BusinessLogic.Domains.LibraryManagement.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.DataAccessComposition.Services;
using Magnetar.Photo.Heimdall.DataAccess.Domains.LibraryCatalog.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.LibraryOnboarding.Services;
using Magnetar.Photo.Heimdall.PresentationLogic.Domains.RemoteDuplicateAnalysis.Services;
using Magnetar.Photo.Heimdall.RemoteAgent.Ssh;
using Microsoft.Extensions.DependencyInjection;

namespace Magnetar.Photo.Heimdall.Desktop;

// The composition root is deliberately the only desktop entry point that knows
// about persistence and physical scanning. Views and view models use PresentationLogic only.
internal static class DesktopComposition
{
    public static LibraryOnboardingPresentationLogicService CreateOnboardingFacade()
    {
        var databasePath = GetCatalogDatabasePath();
        var dataAccess = new ServiceCollection()
            .AddHeimdallDataAccess(databasePath)
            .BuildServiceProvider();
        var catalog = dataAccess.GetRequiredService<ILibraryCatalogDataAccessService>();
        return new LibraryOnboardingPresentationLogicService(new LibraryScanBusinessLogicService(catalog));
    }

    public static RemoteDesktopConfiguration CreateRemoteDuplicateAnalysisFacade()
    {
        RemoteSshConnectionSettings settings;
        try { settings = RemoteSshConnectionSettingsStore.Load(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return new RemoteDesktopConfiguration(null, "", $"No se pudo leer la configuración SSH de Heimdall: {exception.Message}", new RemoteSshConnectionSettings());
        }

        return CreateRemoteDuplicateAnalysisFacade(settings);
    }

    public static RemoteDesktopConfiguration CreateRemoteDuplicateAnalysisFacade(RemoteSshConnectionSettings settings)
    {
        var host = settings.Host;
        var username = settings.Username;
        var keyPath = settings.PrivateKeyPath;
        var fingerprint = settings.HostKeySha256;
        var rootId = settings.RootId;
        var configured = new[] { host, username, keyPath, fingerprint }.Count(value => !string.IsNullOrWhiteSpace(value));

        if (configured == 0)
            return new RemoteDesktopConfiguration(null, rootId, null, settings);
        if (configured != 4)
            return new RemoteDesktopConfiguration(null, rootId,
                "Completa host, usuario, llave privada y huella SHA-256 del host SSH.", settings);

        try
        {
            var port = settings.Port;
            if (port is < 1 or > 65535)
                return new RemoteDesktopConfiguration(null, rootId,
                    "El puerto SSH debe estar entre 1 y 65535.", settings);

            var options = new SshRemoteAgentOptions(
                host!,
                port,
                username!,
                keyPath!,
                fingerprint!,
                settings.AgentCommand);
            options.Validate();

            var timeoutText = Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_TIMEOUT_SECONDS");
            var timeoutSeconds = 24 * 60 * 60;
            if (!string.IsNullOrWhiteSpace(timeoutText) &&
                (!int.TryParse(timeoutText, out timeoutSeconds) || timeoutSeconds is < 30 or > 172800))
                return new RemoteDesktopConfiguration(null, rootId,
                    "HEIMDALL_REMOTE_SSH_TIMEOUT_SECONDS debe estar entre 30 y 172800 segundos (48 horas).", settings);

            var rpc = new SshRemoteAgentRpc(options, timeout: TimeSpan.FromSeconds(timeoutSeconds));
            return new RemoteDesktopConfiguration(new RemoteDuplicateAnalysisPresentationLogicService(rpc), rootId, null, settings);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return new RemoteDesktopConfiguration(null, rootId, $"Configuración SSH inválida: {exception.Message}", settings);
        }
    }

    internal static string GetCatalogDatabasePath()
    {
        var baseDirectory = OperatingSystem.IsLinux()
            ? Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            : null;
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : baseDirectory;
        baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : baseDirectory;

        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new InvalidOperationException("A writable per-user application-data directory is required for the Heimdall catalogue.");
        }

        var catalogueDirectory = Path.Combine(baseDirectory, "Magnetar", "PhotoHeimdall");
        Directory.CreateDirectory(catalogueDirectory);
        return Path.Combine(catalogueDirectory, "heimdall-catalog.db");
    }
}

internal sealed record RemoteDesktopConfiguration(
    RemoteDuplicateAnalysisPresentationLogicService? Service,
    string RootId,
    string? ConfigurationError,
    RemoteSshConnectionSettings Settings);
