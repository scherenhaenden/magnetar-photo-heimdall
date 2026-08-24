# Generate previews and RAW derivatives

## Purpose

Heimdall gives users fast, dependable previews when the original is a large RAW,
offline on a mounted volume, or on a remote computer. It produces
cache-managed thumbnails for browsing and, on request, JPEG/TIFF preview
derivatives for review, sharing or downstream tools. It never modifies,
replaces, or embeds generated data into the original RAW.

This replaces the legacy approach of hard-linking small files and invoking
RawTherapee for larger/RAW inputs. A hard link is not a disposable preview:
removing it can affect the source. Heimdall never uses hard links as preview
output.

## User experience

- Each asset shows a preview state: available, queued, generating, unavailable
  (with reason), stale, offline, or failed.
- The inspector identifies the displayed rendition as embedded or generated,
  shows dimensions and colour profile when known, and states when it is only an
  approximation of a RAW edit.
- From a selection, collection, duplicate group, or import plan, **Generate
  previews** opens a reviewable dialog. It shows scope, execution target,
  processor/profile, format, size/quality, cache/export destination, disk
  estimate, collision policy, and reuse versus regeneration.
- A job view shows per-item status, current host, progress, structured logs,
  actionable errors, and pause, cancel, retry controls. Cancellation prevents
  new work; only completed, verified cache entries survive.
- Users can open, reveal, export, regenerate, pin, or clear a preview. Clearing
  cache never affects originals. Exports outside the cache are explicit
  approved operations with normal journal and undo support.

## Rendition levels

1. **Embedded preview:** use a safe, sufficiently large embedded JPEG/HEIF or
   rendered image for immediate display.
2. **Display thumbnail:** make an orientation-correct, size-bounded grid or
   filmstrip rendition, keyed by source identity and rendering settings.
3. **Review preview:** render JPEG, PNG, or TIFF with explicit long-edge/exact
   bounds, quality and optional colour management.
4. **Export derivative:** a named, intentional output outside cache; it is
   never silently created by scrolling a gallery.

When a source is offline or missing, the last verified preview remains usable
with an offline badge and source revision. Heimdall never claims it represents
a changed original. Without an embedded or generated preview, the asset has a
placeholder and explanation without blocking a catalogue scan.

## Job configuration, reuse and performance

A preview job is an immutable configuration snapshot containing:

- source asset/location IDs plus observed fingerprint/revision;
- requested rendition, dimensions, format, quality, orientation and colour
  space;
- processor adapter/version constraint, named profile/preset and profile digest
  or copied snapshot;
- execution target (local, mounted location, SSH worker), concurrency,
  CPU/GPU/I/O limits, timeout and retry policy;
- cache/export destination, space reservation, overwrite/reuse policy;
- requester, approval, timestamps, per-item outcomes and structured logs.

Heimdall reuses only a verified entry with matching source revision and recipe
fingerprint. Changed source bytes, profile, processor version, or rendition
settings make an entry stale. Interactive previews take priority over
background gallery work. Jobs can resume after restart when the backend supports
it; otherwise temporary output is discarded and the item restarts cleanly.

Before large jobs, Heimdall estimates output space and checks the free capacity
of the host that writes it. Defaults limit parallel RAW renders conservatively;
the user can configure per-host CPU, memory, GPU and I/O budgets.

## Local RAW tooling

Processors are discovered and explicitly approved by the user. Heimdall
validates executable path and version before saving a configuration. It uses an
adapter model, initially supporting:

- embedded-preview/metadata extractors;
- RawTherapee/ART-compatible command-line rendering with a selected profile;
- later adapters such as darktable-cli, dcraw/libraw, vendor SDKs or an internal
  renderer.

An adapter declares extensions, argument schema, profile semantics, version
detection, cancellation method, expected-output validation and GPU capability.
It is run as an executable path plus argument array, restricted working
directory and bounded environment—never a shell command assembled from file
names or user text. Passwords and secrets never enter arguments or logs.

Profiles are read-only job inputs. Heimdall preserves the content or, where
appropriate, a digest and archival copy, enabling reproducibility. Missing or
incompatible software fails individual preview items, not scanning or browsing.

## Local folders, mounts and network shares

A library location records a canonical path/URI, host/volume identity,
case-sensitivity, online state and access capability (read-only/read-write).
Display paths are not a durable identity.

- **macOS:** local APFS/HFS, external disks, SMB/NFS shares and
  `/Volumes/...` mounts. Bookmark/security-scoped access is retained when
  sandboxing requires it, and a remounted disk is revalidated by volume ID.
- **Linux:** local filesystems and accessible fstab, autofs, GVFS/FUSE,
  SMB/CIFS and NFS mounts. A disappeared mount becomes offline; it is never
  scanned as an empty directory.
- **Windows:** local/removable drives and UNC shares
  (`\\server\\share`) are supported. Drive letters are conveniences;
  UNC/share identity plus volume data is retained when available. Backends must
  not assume all network filesystems have Windows' usual case behavior.

For a mounted remote library, users choose local rendering over the mount or
remote rendering via SSH. The UI compares source size, bandwidth/latency,
available processors, cache destination and user policy. Heimdall never copies
RAW files across a network merely to obtain a thumbnail unless the approved job
explicitly requires it.

## SSH workers

An SSH worker allows a server that can directly reach a library to scan or
render locally, then send inventory, preview status and requested derivatives to
the desktop client. It is a versioned, authenticated execution protocol—not
shared database access. Each endpoint owns its own local store.

Worker setup captures verified host key (TOFU requires visible confirmation,
then pinning), account/port, allowed remote roots, worker capabilities,
processor versions and cache policy. Authentication uses the OS SSH agent or a
user-selected key; private keys/passphrases never enter the catalogue, logs or
job records. Password authentication is disabled by default and requires
explicit opt-in if policy permits it.

The client sends an approved, signed job manifest limited to allowed source
paths and recipe data. The worker verifies protocol/schema version, manifest
signature/job identity, permitted roots, traversal and symlink policy, allowed
tools, resource budgets and output destination. It runs locally, streams
structured progress, and returns checksummed results or exposes them through a
configured read-only transfer route. The client verifies host identity, job ID
and output hash before an item becomes usable.

Network loss produces a reconnectable unknown/running state: the client queries
the worker and does not blindly resubmit. A read-only worker is allowed. Remote
preview creation never grants remote reorganization or deletion permission;
those are separate, approved operations with their own authorization policy.

## Cache, provenance and model

Cache data stays in an application-managed or explicit cache root, never next
to originals by default. Entries have opaque IDs, render to private temporary
paths, and are atomically published after validation. Eviction honors capacity,
least-recent use and pin/open/current-job protection, deleting only verified
generated data.

Every rendition records:

- source asset/location ID, source fingerprint/hash, size and observed time;
- recipe fingerprint, processor adapter/name/version, profile reference/hash,
  settings, dimensions, colour and orientation choices;
- local host or worker ID, capability version, job ID, timestamps, outcome,
  output hash/size and cache URI;
- parent rendition if applicable, plus an error code and sanitized diagnostics.

This lineage is visible and exportable as an audit. It establishes precisely
which source and recipe produced an image, including local versus SSH execution.
Shareable diagnostics redact source paths and remote host details unless the
user elects otherwise.

## Security, correctness and recovery

Originals are read-only whenever possible. Output is decoded/validated,
checksummed, and atomically promoted only after completion. Heimdall re-stats
(and re-fingerprints when needed) a source before accepting output; a changed
source makes the result stale, not attached to new bytes.

Tool and SSH output is size-bounded and sanitized. Timeouts, cancellation and
crashes clean only temporary paths inside app-owned cache roots. Configured SSH
host keys, executable paths and allowed roots are reviewable. Workers cannot
request arbitrary desktop paths, and file/profile names cannot inject options.

Corrupt RAWs, malformed embedded previews, codec failures, permission errors,
disconnecting disks, changing mounts and remote-version skew are item-level
failures with an explanatory state and retry path.

## Acceptance criteria

- A mixed JPEG/RAW library is browsable before full RAW rendering completes,
  with embedded/generated/stale/offline status understandable in the UI.
- Users can make reproducible local RAW previews with visible processor/profile
  settings and unchanged originals.
- Local storage, SMB/NFS/UNC mounts and SSH workers all work; a missing mount
  is preserved as offline instead of appearing empty.
- SSH workers accept only validated, bounded, versioned manifests and return
  checksummed outputs—not database files or arbitrary shell commands.
- Cache cleanup removes only generated items while sufficient provenance remains
  to recreate and explain any derivative.

