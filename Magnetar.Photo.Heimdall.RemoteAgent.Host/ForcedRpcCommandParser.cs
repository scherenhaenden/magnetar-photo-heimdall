using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Magnetar.Photo.Heimdall.RemoteAgent.Host;

internal static class ForcedRpcCommandParser
{
    internal const int MaximumRequestBytes = 1024 * 1024;
    private const string CommandPrefix = "heimdall-agent rpc ";
    private const int MaximumEncodedRequestLength = ((MaximumRequestBytes + 2) / 3) * 4;
    private static readonly Regex TokenPattern = new("\\A[A-Za-z0-9_-]+\\z", RegexOptions.CultureInvariant);
    private static readonly Regex CommandPattern = new("\\Aheimdall-agent rpc ([A-Za-z0-9_-]+)\\z", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string ExtractToken(string[] args, string originalCommand)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != 1 || args[0] != "forced")
            throw new InvalidDataException("Only SSH forced-command mode is supported.");
        return ExtractToken(originalCommand);
    }

    internal static string ExtractToken(string originalCommand)
    {
        ArgumentNullException.ThrowIfNull(originalCommand);
        if (originalCommand.Length > CommandPrefix.Length + MaximumEncodedRequestLength)
            throw new InvalidDataException("SSH command exceeds the RPC request size limit.");
        var match = CommandPattern.Match(originalCommand);
        if (!match.Success) throw new InvalidDataException("SSH command is outside the forced RPC allowlist.");
        return match.Groups[1].Value;
    }

    internal static string DecodeRequestToken(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length is 0 or > MaximumEncodedRequestLength || !TokenPattern.IsMatch(token))
            throw new InvalidDataException("RPC request token is invalid or exceeds the size limit.");
        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - base64.Length % 4) % 4);
        var bytes = Convert.FromBase64String(base64);
        if (bytes.Length > MaximumRequestBytes)
            throw new InvalidDataException("RPC request exceeds the size limit.");
        return StrictUtf8.GetString(bytes);
    }

    internal static string SerializeFailure(JsonSerializerOptions jsonOptions) =>
        JsonSerializer.Serialize(new { success = false, error = "Remote agent request failed." }, jsonOptions);
}
