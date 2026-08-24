# Magnetar Photo Heimdall

Local-first photo-library organizer and cleanup cockpit. Heimdall will catalog a
library, make organization and duplicate-cleanup plans visible, and apply only
the actions a person approves.

This repository intentionally starts with product and architecture decisions,
not an application scaffold. The existing projects remain independent:

- **Photo Culler** remains the focused review/culling tool. Heimdall will be
  able to launch it and exchange a small, versioned local contract.
- **PhotoLibrarizer** is research input for ordering rules, metadata fallback,
  and collision handling. Its code is not copied into this project.
- **Photos** is research input for duplicate detection and sorting experiments.
  Its destructive deletion routines are explicitly not reused.

## Recommended first implementation

Use **C# / .NET 10, Avalonia, and SQLite** for the first working product.
It is the shortest path to a native cross-platform desktop application, fits
the existing photo experiments, and has mature metadata and image libraries.
Keep the core behind interfaces so a Rust scanner/hasher sidecar can be added
later if measurements justify it. A Rust rewrite is not a prerequisite.

## First usable slice

1. Add library roots and scan them without changing files.
2. Persist assets, locations, metadata provenance, hashes, thumbnails, and
   diagnostics in SQLite.
3. Browse a grid and filter by date, camera, missing metadata, and duplicate
   group.
4. Preview organization and cleanup plans before anything is changed.
5. Copy, verify, quarantine, journal, and undo approved operations.
6. Launch Photo Culler and exchange selected asset paths/decisions through a
   versioned localhost or CLI contract.

See [analysis](docs/ANALYSIS.md) and the [architecture](docs/ARCHITECTURE.md).

## Functional product specification

The legacy `Photos` experiments and `Photos/PhotoLibrarizer` contain the
behaviour that Heimdall must consolidate. Their user-facing requirements are
documented individually in [docs/features/README.md](docs/features/README.md).
Those documents are specifications for Heimdall, not a promise to preserve the
legacy implementation or its unsafe file mutations.

Performance-critical components and the evidence required before moving one
from .NET to Rust are indexed in [docs/performance/README.md](docs/performance/README.md).
