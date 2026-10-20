namespace Magnetar.Photo.Heimdall.RemoteAgent.Ssh;

/// <summary>SSH endpoint and trust settings. Private keys remain in the OS/user key store.</summary>
public sealed record SshRemoteAgentOptions(
    string Host,
    int Port,
    string Username,
    string PrivateKeyPath,
    string ExpectedHostKeySha256,
    string AgentCommand = "heimdall-agent rpc")
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Username) ||
            string.IsNullOrWhiteSpace(PrivateKeyPath) || string.IsNullOrWhiteSpace(ExpectedHostKeySha256))
            throw new ArgumentException("Host, username, key path, and pinned SHA-256 host fingerprint are required.");
        if (Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
        const string prefix = "SHA256:";
        var pin = ExpectedHostKeySha256;
        if (!pin.StartsWith(prefix, StringComparison.Ordinal) || pin.Length != prefix.Length + 43 ||
            pin.AsSpan(prefix.Length).Contains('=') ||
            pin.AsSpan(prefix.Length).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/".AsSpan()) >= 0)
            throw new ArgumentException("Host key pin must use OpenSSH SHA256:<43-character-base64> format.", nameof(ExpectedHostKeySha256));
        if (string.IsNullOrWhiteSpace(AgentCommand) || AgentCommand.Any(c =>
                !(char.IsAsciiLetterOrDigit(c) || c is '/' or '.' or '_' or '-' or ' ')))
            throw new ArgumentException("Agent command may contain only executable path characters and spaces; shell syntax is forbidden.", nameof(AgentCommand));
        if (!AgentCommand.EndsWith(" rpc", StringComparison.Ordinal) || AgentCommand.Length <= 4 ||
            AgentCommand[..^4].Contains(' '))
            throw new ArgumentException("Agent command must be one executable path followed by the fixed 'rpc' subcommand.", nameof(AgentCommand));
    }
}
