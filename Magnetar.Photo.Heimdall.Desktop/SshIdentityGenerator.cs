using System.Diagnostics;

namespace Magnetar.Photo.Heimdall.Desktop;

/// <summary>Creates an optional per-user Ed25519 identity. Called only after an explicit UI action.</summary>
internal static class SshIdentityGenerator
{
    public static async Task<string> CreateDedicatedEd25519Async(CancellationToken cancellationToken = default)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) throw new InvalidOperationException("No se pudo localizar la carpeta del usuario.");
        var sshDirectory = Path.Combine(home, ".ssh");
        var directoryAlreadyExisted = Directory.Exists(sshDirectory);
        Directory.CreateDirectory(sshDirectory);
        if (!directoryAlreadyExisted && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(sshDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var privateKeyPath = Path.Combine(sshDirectory, "heimdall_nas");
        var publicKeyPath = privateKeyPath + ".pub";
        if (File.Exists(privateKeyPath) || File.Exists(publicKeyPath))
            throw new IOException($"Ya existe una llave Heimdall en {privateKeyPath}; no se sobrescribió.");

        var startInfo = new ProcessStartInfo("ssh-keygen")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-q", "-t", "ed25519", "-a", "64", "-N", "", "-C", "heimdall-nas", "-f", privateKeyPath })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar ssh-keygen.");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var error = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0 || !File.Exists(privateKeyPath) || !File.Exists(publicKeyPath))
        {
            TryDelete(privateKeyPath);
            TryDelete(publicKeyPath);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "ssh-keygen no pudo crear la llave dedicada."
                : $"ssh-keygen no pudo crear la llave dedicada: {error.Trim()}");
        }

        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privateKeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return privateKeyPath;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
