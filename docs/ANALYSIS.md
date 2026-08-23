# Source-project analysis

## What to carry forward

| Source | Valuable concepts | Heimdall treatment |
| --- | --- | --- |
| Photo Culler | local catalog, scan revisions, jobs, identity levels, RAW/JPEG pairing, metadata/session/burst grouping, thumbnails, transparent decisions | Keep Heimdall's catalog separate; integrate Culler as an external module first. |
| PhotoLibrarizer | recursive scanning, EXIF/XMP/QuickTime date fallback, configurable folder/file patterns, content-hash collision handling, camera grouping | Reimplement as a declarative rule engine with a previewable operation plan. |
| Photos experiments | prefilter duplicates by size, verify content equality, date-based naming and destination folders | Use for discovery only; every outcome must be reviewed and reversible. |

## Safety decisions

The prior ordering experiments sometimes delete duplicates during discovery or
collision handling. Heimdall never performs that mutation. Its only mutation
path is:

`scan → catalog → propose plan → review → copy → verify hash → quarantine → journal → undo`

Permanent deletion is out of scope for the first release. A "duplicate" is a
candidate until content verification and an explicit keeper decision have both
occurred. Metadata dates preserve their provenance and confidence: DateTimeOriginal,
then XMP/QuickTime, then filesystem mtime.

## Boundaries

Do not share a SQLite file or import application internals between Heimdall and
Photo Culler. The initial interoperability surface should be a local,
versioned contract:

- `GET /health` for availability and supported contract versions.
- `POST /v1/culling-jobs` with asset IDs, canonical paths, and optional
  originating library ID.
- `GET /v1/culling-jobs/{id}/decisions` for decisions translated to explicit,
  explainable suggestions.
- OS deep links/CLI launch as a fallback when the local service is absent.

The host must always let either application be opened from the other, without
making either unable to work alone.

## Technology decision

Choose modern C#/.NET + Avalonia + SQLite now. It reuses the user's existing
strengths and mature metadata tooling while avoiding the obsolete .NET
Framework prototype architecture. Rust remains a measured future option for
isolated high-throughput services such as scanning, hashing, or thumbnails;
it is not the product's primary dependency today.
