# Architecture direction

## Modules

```text
Avalonia desktop UI
        │
Application services ── Integration gateway ── Photo Culler
        │                         (local API / CLI / deep link)
Domain: assets, locations, identities, rules, plans, journal
        │
SQLite catalog ── thumbnail store ── filesystem adapters
```

### Catalog ownership

Heimdall owns its library and operational data:

- assets may have multiple observed locations;
- content identities are cached by fast fingerprint plus verified BLAKE3;
- metadata values include source/provenance and confidence;
- plans and every applied operation are immutable journal entries;
- the UI reads through application queries, never raw database tables.

### Organization-rule engine

Rules turn an asset into a proposed destination, for example:

```text
{year}/{month}/{day}/{datetime}_{camera}_{content-hash:8}{extension}
```

Rules must detect collisions before execution. An existing identical verified
content hash creates a duplicate candidate; a different hash receives a
deterministic suffix. Camera folders and no-date handling are optional rules,
not hardcoded behavior.

### Operations

All file actions run as durable operations with progress and cancellation.
Moves are implemented as copy + hash verification + source quarantine, so
failure does not destroy the only source. Undo restores from quarantine using
the journal. The system makes no direct delete call in its first release.

## Delivery sequence

1. Catalog schema, migrations, index-only scanner, diagnostics.
2. Metadata, thumbnail job, asset grid/search/filter.
3. Identity/hash cache and duplicate review groups.
4. Rule editor and dry-run organization plan.
5. Apply, quarantine, audit journal, undo.
6. Photo Culler local integration and reciprocal launch actions.
