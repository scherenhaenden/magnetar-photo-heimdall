# Remote SSH agent: current implementation and deployment gate

The repo now contains the client transport (`RemoteAgent.Ssh`) and a NAS-side
RPC executable (`RemoteAgent.Host`). The desktop/client sends a typed JSON
request encoded as base64url. The remote account must be restricted to the
agent with an SSH `authorized_keys` forced command; do not expose an interactive
shell to the Heimdall key.

## Operations implemented

- `Scan`: returns image-file paths, lengths, and timestamps under the requested
  configured root.
- `Hash`: computes streaming BLAKE3 evidence.
- `DuplicateScan`: the existing Heimdall grouper first excludes unique file
  sizes, then hashes every byte for remaining candidates with bounded
  concurrency. Results contain root-relative paths only.
- Every accepted/rejected result is journaled in a NAS-local SQLite database;
  repeated idempotency keys return the saved receipt. A receipt includes the
  JSON result so the caller can persist it to Heimdall's catalog.
- The host also maintains the Heimdall catalog in a separate NAS-local SQLite
  file. Configured roots are registered by canonical path and keep a stable
  library ID across agent restarts. Each complete scan reconciles missing
  catalog rows only after the filesystem traversal succeeds. Duplicate scans
  use `LibraryDuplicateAnalysisBusinessLogicService`, so only repeated-size
  candidates are fully hashed and new BLAKE3 evidence is persisted.

No RPC accepts an arbitrary shell command, absolute path, delete, move, or
quarantine request. Roots are configured by the NAS operator. Path traversal,
symlinks/reparse points, and unconfigured roots are rejected. On Linux the
agent reads thermal zone `temp` values from `/sys/class/thermal`; if no valid
sensor can be read, new work is paused. The controller uses 70/80/90 C
warning/high/critical thresholds, reduces concurrency on warning, refuses new
work at high/critical/unavailable, and requires a continuous cool-down after
critical. Thresholds still need tuning against the actual ASUSTOR sensors.

## NAS-side configuration

Create an operator-owned JSON config outside photo roots, for example:

```json
{
  "journalPath": "/volume1/.heimdall/agent-receipts.sqlite",
  "catalogPath": "/volume1/.heimdall/photo-catalog.sqlite",
  "maximumConcurrency": 2,
  "thermalSensorDirectory": "/sys/class/thermal",
  "roots": [
    { "id": "photos", "displayName": "Photos", "path": "/volume1/Edward/Photos" }
  ]
}
```

Set `HEIMDALL_AGENT_CONFIG` to this file for the agent process. The agent
creates its SQLite parent directory and schema at startup. Configure a
dedicated SSH account with read-only access to photo roots and write access
only to the journal directory. Restrict its public key to the fixed wrapper,
disable PTY, agent forwarding, TCP forwarding, and X11 forwarding. The wrapper
must execute `/path/to/heimdall-agent forced`; this mode validates
`SSH_ORIGINAL_COMMAND` against the single `heimdall-agent rpc <base64url>`
shape before decoding the typed request. Do not use the development `rpc`
entry point for an unrestricted login account. The executable accepts only
the `forced` entry point; it has no direct `rpc <token>` mode. For local
development, use the Host tests, which instantiate the agent against temporary
roots, or emulate `SSH_ORIGINAL_COMMAND` while invoking `forced`.

Publish self-contained for the NAS architecture and install the executable and
config manually only after reviewing the target paths. Do not run this agent
against a mounted Mac path: the agent itself must run on the NAS so file reads
and BLAKE3 hashing occur there.

## Client configuration and auth

The Desktop composition root enables the remote duplicate-analysis panel when
these environment variables are set:

| Variable | Required | Default / meaning |
|---|---:|---|
| `HEIMDALL_REMOTE_SSH_HOST` | yes | NAS hostname or IP |
| `HEIMDALL_REMOTE_SSH_PORT` | no | `22` |
| `HEIMDALL_REMOTE_SSH_USERNAME` | yes | Restricted agent account |
| `HEIMDALL_REMOTE_SSH_KEY_PATH` | yes | Client private-key path |
| `HEIMDALL_REMOTE_SSH_HOST_KEY_SHA256` | yes | Independently verified `SHA256:` fingerprint |
| `HEIMDALL_REMOTE_AGENT_COMMAND` | no | `heimdall-agent rpc` |
| `HEIMDALL_REMOTE_SSH_TIMEOUT_SECONDS` | no | `86400` (24 hours); accepted range 30–172800 seconds |
| `HEIMDALL_REMOTE_ROOT_ID` | no | Initial authorized agent root ID; can also be entered in the UI |

The timeout covers a whole RPC request, including a large `DuplicateScan`, so
the default is intentionally long. SSH authentication uses a key, never the
NAS account password. A passphrase provider may retrieve a key passphrase from
the OS keychain; the transport never writes credentials to SQLite. The host-key event rejects
connections on any mismatch. Requests honor cancellation. The server must use
the forced-command wrapper above. The UI submits read-only analyses only and
renders every returned duplicate group with its root-relative file paths.

At the transport boundary, `SshRemoteAgentRpc` receives hostname/IP, port,
username, private-key path, and the independently verified fingerprint. A
passphrase provider may prompt or retrieve the passphrase from the OS keychain;
the transport never writes credentials to SQLite. The host-key event rejects
connections on any mismatch. Requests use a bounded timeout and honor
cancellation. The server must use the forced-command wrapper above.

## Validation status

The host and client compile locally; local host tests use only temporary test
files and verify size-first exact duplicate grouping, root-relative results,
idempotent receipts, traversal rejection, and forced-mode argument validation.
SSH-client tests verify endpoint/pin/command configuration validation,
cancellation before opening a connection, and bounded wire-codec behavior;
they do not establish an SSH connection or exercise the actual host-key event.
A self-contained `linux-x64` single-file publish has now completed locally from
the existing NuGet assets; its SHA-256 manifest verified successfully, and the
executable is an ELF x86-64 binary. The NAS was previously observed as Linux
x86_64 with glibc 2.39, which matches the target's expected ABI, but the
executable has not yet been run on the NAS. No SSH
handshake, ASUSTOR runtime, thermal sensor, or deployment has been tested yet.
Deployment must still verify the forced-command wrapper, permissions, host-key
fingerprint, and available disk space for the journal. Do not enable any
destructive operation from this agent.
