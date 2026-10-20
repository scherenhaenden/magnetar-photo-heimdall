using System.Text.Json;

namespace Magnetar.Photo.Heimdall.Desktop;

/// <summary>Non-secret SSH connection settings. Private key contents and passphrases are never serialized.</summary>
public sealed record RemoteSshConnectionSettings(
    string Host = "",
    int Port = 22,
    string Username = "",
    string PrivateKeyPath = "",
    string HostKeySha256 = "",
    string AgentCommand = "heimdall-agent rpc",
    string RootId = "");

internal static class RemoteSshConnectionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string GetPath()
    {
        var dataDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(dataDirectory))
            throw new InvalidOperationException("No se encontró una carpeta de datos privada para Heimdall.");
        return Path.Combine(dataDirectory, "Magnetar", "PhotoHeimdall", "remote-ssh.json");
    }

    public static RemoteSshConnectionSettings Load()
    {
        var path = GetPath();
        if (File.Exists(path))
            return JsonSerializer.Deserialize<RemoteSshConnectionSettings>(File.ReadAllText(path), JsonOptions)
                   ?? new RemoteSshConnectionSettings();

        // Keep existing environment-based installations working; persist only when the user saves in the UI.
        return new RemoteSshConnectionSettings(
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_HOST") ?? "",
            ReadPort(),
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_USERNAME") ?? "",
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_KEY_PATH") ?? "",
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_HOST_KEY_SHA256") ?? "",
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_AGENT_COMMAND") ?? "heimdall-agent rpc",
            Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_ROOT_ID") ?? "");
    }

    public static void Save(RemoteSshConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = GetPath();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            RestrictFile(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            RestrictFile(path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static int ReadPort() => int.TryParse(Environment.GetEnvironmentVariable("HEIMDALL_REMOTE_SSH_PORT"), out var port) ? port : 22;

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
