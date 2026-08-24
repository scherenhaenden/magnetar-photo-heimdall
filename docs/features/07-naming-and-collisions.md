# Name files and resolve collisions

## Purpose

This feature turns an asset and a destination layout into a **fully previewable
destination path**. It is used by organization plans, imports, renames and
remote execution. Its job is deliberately narrow: derive names consistently,
identify every ambiguity before an operation begins, and make the chosen
resolution repeatable. It never overwrites an existing file and never deletes a
source as a side effect of naming.

PhotoLibrarizer-style date naming remains useful, but Heimdall makes it a plan:
the user sees the exact source, proposed target, reason, collision class and
chosen action for every affected file before approving execution.

## User-visible workflow

1. The user selects a saved naming policy or creates one in the organization
   plan. The editor includes a live preview using representative assets.
2. Heimdall evaluates all planned assets without changing the filesystem. The
   plan reports valid targets, warnings, blocked paths and collision groups.
3. The review table filters by collision, warning, source root, endpoint, file
   type or decision. A detail panel explains which token or platform rule made a
   path invalid or ambiguous.
4. For each collision group, the user chooses a keeper/action or accepts an
   explicit deterministic default. Bulk actions apply only to compatible groups.
5. Approval freezes the policy revision and resolved destination. Execution
   revalidates source and destination state; a change becomes a conflict, never
   an implicit overwrite.

The interface must always distinguish *proposed path* from *actual path* and
show paths as they exist on the target endpoint. A copy-path control copies the
native spelling; an optional normalized form is available for diagnostics.

## Naming policy and rule language

A policy is versioned, named and immutable once used by an approved plan. It
contains folder and filename templates, extension policy, transliteration and
sanitization options, time-source precedence, sequence scope and fallbacks.

```text
folders:  {captureYear}/{captureMonth:02}/{captureDate:yyyy-MM-dd}
filename: {captureDate:yyyy-MM-dd}_{captureTime:HH-mm-ss}_{cameraModel}_{hash:8}
```

Templates are declarative only: no shell expansion, expressions, arbitrary code
or environment-variable interpolation. Literal text is permitted; `/` denotes a
folder boundary in portable templates. The evaluator rejects absolute paths,
`.`/`..` components, empty components and traversal after normalization.

| Family | Examples | Notes |
| --- | --- | --- |
| Capture/import time | `{captureDate}`, `{captureYear}`, `{captureMonth:02}`, `{captureTime}`, `{importDate}` | Time source and timezone are stored with the result. |
| Source identity | `{originalName}`, `{originalStem}`, `{extension}`, `{sourceRoot}` | Input is sanitized; extension is separately governed by default. |
| Metadata | `{cameraMake}`, `{cameraModel}`, `{lens}`, `{rating}`, `{keyword}` | Missing values invoke a declared fallback. |
| Identity/disambiguation | `{hash:8}`, `{assetId}`, `{sequence:03}` | Hash needs the required verified-hash stage; sequence is deterministic. |
| Fixed plan data | `{planName}`, `{libraryName}` | Values are sanitized under the target policy. |

Formatting is typed and limited (numeric zero padding and explicit date/time
formats). Unknown tokens, malformed formats and unavailable required metadata
block the item with a precise error. Each optional token declares a fallback:
fixed text such as `Unknown-date`, omit-with-separator cleanup, import time, or
require user input. Default time precedence is verified embedded capture time,
then filesystem time with provenance, then import time; timezone conversion is
visible and never rewrites source metadata.

`{sequence}` is allocated only after sorting the full plan by stable asset ID in
its declared scope (for example target day/folder). An unchanged plan always
gets the same result. It is never a race-prone “next free number” at execution.

`{hash:n}` is an abbreviation of the asset's full **BLAKE3** content hash, with
the hash algorithm/version recorded in the plan. It replaces legacy MD5 naming:
new scans do not calculate MD5, and an old MD5 value is never used to verify a
copy or establish equality. `n` is configurable (recommended: 12 hexadecimal
characters for a filename suffix); a BLAKE3 prefix collision becomes an
in-plan collision and is resolved with a longer prefix or sequence. A short
prefix is a naming component only—the full BLAKE3 remains mandatory for
integrity and duplicate decisions.

### Extension policy and related files

The default preserves source extension bytes/case while matching names
case-insensitively only where the target requires it. A policy can normalize to
lowercase or map known aliases (`jpeg` → `jpg`), but never change file format or
invent an extension. RAW files, XMP sidecars, Live Photo companions and other
linked assets are renamed as an atomic family: the common stem changes together,
and partial families are explicitly warned about or blocked.

## Target paths and endpoint portability

Every destination has an endpoint with declared capabilities, probed where
possible and rechecked during execution. Local folders, mounted shares and SSH
agents share the same logical plan; endpoint adapters map its portable relative
path to a native path.

| Target | Path and naming concerns |
| --- | --- |
| macOS / Linux local filesystem | POSIX separators and `/` root; NUL and `/` cannot appear in a filename. Volumes may be case-sensitive or case-insensitive, so probe the volume rather than inferring from OS. |
| Network mount (SMB, NFS, FUSE, NAS) | Behaves as the mounted filesystem, not the client OS. Record mount/share identity, available space and probed case/Unicode behaviour; a changed mount is a conflict. |
| Windows local filesystem | Uses native `\\` paths at execution while templates remain `/`-separated. Reject reserved device names (`CON`, `NUL`, `COM1` etc.), `< > : " / \\ | ? *`, trailing space/dot components and drive/UNC escapes. Detect rather than assume long-path support. |
| SSH-managed endpoint | Client sends a versioned plan containing logical relative paths. The server agent resolves them beneath configured allowed roots using its own platform rules, revalidates capabilities and returns canonical display paths plus conflicts. No remote shell command is built from filenames. |

Portable mode applies the strict intersection of every selected target’s rules.
Endpoint-native mode permits a policy for one target but flags it non-portable if
reused elsewhere. Unicode is normalized for comparison by endpoint capability;
the stored record retains display spelling. Case folding follows actual target
behaviour, never UI locale. Heimdall detects case-only aliases, Unicode-equivalent
names, reserved names and components with equal comparison keys.

A case-only rename on a case-insensitive target uses a unique temporary,
in-root name and is journaled as one reversible logical operation. If a component
or full path is too long, preview shows its limit and offending portion. The user
may alter the policy, select deterministic shortening (truncate stem plus
`-{hash:8}`), or skip; silent truncation is forbidden.

## Collision detection and decisions

Evaluation occurs after expansion, sanitization, normalization and endpoint
comparison. It considers planned targets and existing destination entries. A
group includes all matching assets, current target entry, linked families and
the evidence used to compare content.

| Class | Detection | Allowed resolution |
| --- | --- | --- |
| In-plan naming collision | Two or more assets yield the same target key | Change rule, deterministic sequence/hash suffix, or skip. |
| Existing exact duplicate | Verified content hash equals target hash | Keep existing, retain incoming elsewhere, or mark incoming duplicate candidate. Never auto-delete. |
| Existing different content | Target exists and verified hashes differ | Deterministic incoming suffix, another name/folder, or skip. |
| Case/Unicode alias | Display paths share target comparison key | Explicitly rename one; do not rely on spelling. |
| Directory/file shape conflict | Required folder is file or target file is folder | Use another target or skip; never replace. |
| Linked-family conflict | Sidecar/companion conflicts differently | Resolve family together or block it. |
| State changed since preview | Source, target, capacity or endpoint state differs | Pause group for renewed review. |

“Identical” requires the plan’s verification standard—normally a full
cryptographic hash, not matching names, sizes or quick hashes. Unavailable hash
evidence is shown as *unverified* and cannot justify deduplication. The preferred
different-content suffix is verified full-hash-derived (`IMG_0001-a1b2c3d4.jpg`).
If its prefix is not unique in group, it lengthens deterministically; then uses
a stable asset-ID segment. Numeric suffixes are allowed only from frozen plan
sequence allocation. Random suffixes are never used.

## Data model and auditability

The catalog stores policy revision and evaluation evidence, not merely final
strings:

- `NamingPolicy`: ID/revision, templates, token/fallback, sanitization,
  extension, sequence and portability settings.
- `NameEvaluation`: asset/location, policy revision, metadata snapshot IDs,
  expanded components, logical relative path, native display path, comparison
  key, warnings/errors and timestamp.
- `CollisionGroup`: endpoint, comparison key, members, existing fingerprint/hash
  evidence, class, user decision and resolution algorithm.
- `PathReservation`: approved plan, endpoint, comparison key and target; protects
  concurrent plan creation but is not an existing file.
- `OperationJournal`: preflight observations, source/destination fingerprints,
  chosen and temporary paths, verification results and undo data.

Path strings, comparison keys and filesystem identifiers are distinct. Display
paths never define a security boundary. Endpoint/root IDs prevent remote or
remounted paths being mistaken for local ones.

## Safety, concurrency and execution

Planning is read-only. Approval reserves paths and freezes policy/collision
choices. Before each operation, executor resolves beneath approved root,
validates source identity and rechecks destination. A mismatch becomes
`NeedsReview`; it never applies an outdated decision.

Moves/copies use the project safe-operation protocol: copy to an in-root
temporary target (or valid same-filesystem atomic rename), verify required hash,
atomically publish where supported, journal, then quarantine source only with
explicit approval. Existing targets are never replaced, even if hashes match.
Undo validates that no unrelated file has occupied its destination.

Remote agents use mutually authenticated SSH transport and a versioned RPC/plan
contract. They receive only relative paths and approved operation IDs, enforce
allowlisted roots and execute local filesystem actions locally. They return
structured capabilities, progress, conflicts and journal evidence—never execute
arbitrary command text. On disconnect, remote journal evidence is authoritative
for recovery and client reconciliation.

## Acceptance criteria

- Users can preview/export every source → target mapping, warning and decision
  without filesystem mutation.
- Frozen policy, asset snapshots and endpoint capabilities produce identical
  mappings and suffixes on re-evaluation.
- macOS, Linux, Windows, mounts and SSH targets use portable-safe names or are
  clearly rejected as endpoint-native.
- Case-only, Unicode-equivalent, invalid, reserved and overlong paths are found
  before approval.
- Changed target/source/mount/server state pauses only affected work and cannot
  overwrite data.
- Each executed rename/move/copy traces from policy and collision choice through
  verification to journaled undo information.
