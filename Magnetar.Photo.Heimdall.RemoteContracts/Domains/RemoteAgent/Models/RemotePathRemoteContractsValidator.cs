namespace Magnetar.Photo.Heimdall.RemoteContracts.Domains.RemoteAgent.Models;

/// <summary>
/// Validates relative paths used in OperationRequestRemoteContractsModel.
/// Absolute paths, backslashes, null bytes, empty segments, and traversal
/// sequences are all rejected at the contract layer before any I/O occurs.
/// </summary>
public static class RemotePathRemoteContractsValidator
{
    public static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096
                                            || Path.IsPathRooted(path)
                                            || IsWindowsDriveQualified(path)
                                            || path.Contains('\\')
                                            || path.Contains('\0'))
        {
            throw new RemoteContractsValidationException(
                "A non-empty, slash-separated relative path without backslashes or null bytes is required.");
        }

        var segments = path.Split('/', StringSplitOptions.None);
        if (segments.Any(s => s is "" or "." or ".."))
        {
            throw new RemoteContractsValidationException(
                "Path traversal sequences ('..'), empty segments, and '.' are not allowed.");
        }
    }

    private static bool IsWindowsDriveQualified(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/';
}