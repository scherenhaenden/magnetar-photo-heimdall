# Inspect metadata and dates

## User outcome

Heimdall gives every catalogued asset a transparent, reviewable description of
when it was captured, where that conclusion came from, and how reliable it is.
The selected **organization date** is the date used by a date-based rule; it is
never silently confused with an import, file-copy, or filesystem date. Users
can inspect, search, correct, and deliberately choose this value before it is
used to name or relocate a photo.

This specification keeps PhotoLibrarizer's central organizing behaviour--read
the media date, derive a `yyyy_MM_dd_HH_mm_ss`-style name, and arrange files
by date--but makes source selection, ambiguity, and correction visible. It is
also used consistently for local folders, mounted/network libraries, and a
remote library catalogued by an approved Heimdall agent over SSH.

## What the feature does

### Extract and preserve facts

During a read-only scan, Heimdall extracts available metadata without changing
the source file. It stores both normalized values and enough original evidence
to explain them:

- EXIF: `DateTimeOriginal` (36867), `DateTimeDigitized` (36868), `DateTime`
  (306), offset tags, camera make/model, lens, orientation, dimensions, GPS
  and applicable software fields.
- XMP/IPTC: creation, digital-creation, modify dates, timezone offsets,
  title, rating, keywords and creator fields where the parser supports them.
- Video/container data: QuickTime/ISO media creation, track creation and
  modification times; it labels these as container dates, not camera EXIF.
- Filesystem observations: birth/creation time when supplied by the
  filesystem, last modified time, byte length, filename, path and scan time.
- Sidecars: an associated `.xmp` sidecar is recorded as a separate source;
  it is never mistaken for embedded metadata. Pairing rules and precedence are
  explicit and the association is shown in the detail view.

The raw tag name, unparsed raw value, parser/provider and read timestamp are
retained. Re-scans update extracted observations rather than erasing historical
user corrections or the evidence of a prior parse error.

### Choose a default capture date

Heimdall calculates a `SuggestedCaptureDate` and a confidence level. The
default precedence is:

1. Valid EXIF `DateTimeOriginal`, with a valid EXIF/XMP timezone offset when
   present.
2. Valid XMP/IPTC original or digital-creation date, including an associated
   sidecar according to the library's sidecar policy.
3. Valid `DateTimeDigitized` or a video/container media-creation date.
4. Valid EXIF `DateTime` only when a more capture-specific value is absent.
5. Filesystem creation/birth time, if the provider offers a trustworthy value.
6. Filesystem modification time as the last automatic fallback.

`DateTimeOriginal` without an offset is still normally the best indication of
the camera's local wall-clock time, but it has lower confidence than an
equivalent date with a trustworthy offset. A filesystem timestamp is always
displayed as a fallback, never as “captured” without qualification. Scan/import
time is not a capture-date candidate.

If a source contains only a calendar date, Heimdall preserves that precision:
it must not invent midnight and present it as an exact time. Organization rules
may use a known year/month/day while requiring user review before generating a
time-based filename.

### Detect conflicts and uncertainty

The feature compares usable capture candidates after normalizing known offsets.
It raises an explainable diagnostic when dates materially disagree (default:
more than five minutes for precise values, or incompatible calendar days for
date-only values). Typical reasons include an incorrect camera clock, a copied
file with a new mtime, an edited export, or a sidecar differing from embedded
EXIF.

Confidence is presented as **high**, **medium**, **low**, or **needs review**:

- High: original-capture metadata with parseable offset, or several independent
  capture sources agreeing.
- Medium: an original-capture field without offset, or a credible XMP/container
  creation field.
- Low: `DateTime` or filesystem creation time used as fallback.
- Needs review: no usable date, parse failure, an invalid value, or a detected
  conflict that can affect the selected date.

The precise rule and candidate values must be visible; confidence is an aid to
review, not a hidden scoring system. Libraries may tighten the conflict
threshold, but the plan records the threshold and catalog revision used.

## User experience

### Asset detail

The asset detail panel shows a compact summary first: selected organization
date, precision, timezone state, confidence, source and any warning. An
expandable “date evidence” section lists every candidate in precedence order,
its raw value, normalized value, source location (embedded tag, sidecar,
filesystem, or remote observation), parser result, and why it did or did not
win. The general metadata section remains browseable and searchable without
forcing users to understand EXIF tag numbers.

The user can filter the library by missing date, low confidence, conflict,
date source, camera, date range and whether an override exists. A bulk review
queue highlights items that a chosen organization plan would send to its
no-date path or name with an imprecise time.

### Corrections and overrides

“Use a different date” creates a non-destructive user assertion with a value,
precision, optional timezone, scope, author/time, and optional note. It never
writes EXIF/XMP into the original in the first release. The interface offers:

- asset-wide override, used by future plans unless superseded;
- location-specific override, for a particular rendition whose metadata is not
  representative of the logical asset;
- plan-only override, for one organization/export plan; and
- “restore automatic selection”, which disables the assertion and returns to
  the calculated suggestion without deleting its audit history.

Bulk correction is allowed only after the preview shows its selection and the
date transformation (for example, apply a camera-clock offset to selected
assets). The original extracted facts stay visible alongside the override, and
all plans state whether each date was automatic or user-selected.

## Timezone and platform behaviour

Heimdall stores an instant only when an offset is known. Offset-free camera
values are stored as a local date/time plus an `offset unknown` state, so they
are not accidentally shifted when a Mac, Linux host, Windows PC, SMB/NFS mount,
or remote server is in another timezone. Display uses the user's chosen view
timezone while preserving the original wall-clock representation and source
offset. DST-invalid or ambiguous local times are flagged instead of guessed.

For mounted and network storage, the cataloguing client treats filesystem
birth and modified times as provider observations, records the mount/provider
identity and filesystem resolution, and downgrades their confidence when the
provider cannot guarantee semantics. For a remote SSH agent, extraction occurs
on the server next to the files; it returns versioned metadata evidence,
server timezone/clock information, and a stable remote location identifier to
the local client. The local client applies the same precedence and retains the
agent/parser versions. Remote filesystem timestamps must never replace embedded
capture metadata merely because they are easier to obtain.

Windows has no universal POSIX birth-time semantics across NTFS, SMB and mapped
drives, just as macOS/Linux differ by filesystem and mount protocol. Heimdall
therefore records the reported creation/modified timestamps with their source
rather than assuming any platform's “created” field is a real photo capture
time.

## Catalog model and lifecycle

Metadata belongs to an immutable-ish extraction observation associated with an
asset location and scan revision. Normalized candidates include: kind, raw
value, parsed local value or instant, precision, offset state, source, parser
version, validity, confidence contribution, and diagnostic links. The asset
holds the current computed suggestion; a user assertion and a plan selection
are separate records, so re-scanning cannot overwrite a correction and a
one-off plan cannot unexpectedly change future organization.

When content is copied, moved, backed up, or discovered at a new location, the
catalogue retains source provenance and re-extracts metadata only as needed.
Changes in extracted metadata produce a new observation and may mark a prior
organization proposal stale. Derived exports show their own creation metadata
but link back to the selected source asset/date; they are not evidence that the
original was captured at export time.

## Failure handling and safety

Unreadable files, unsupported formats, malformed EXIF, invalid dates,
permission failures, unavailable mounts, remote-agent protocol errors, and
sidecar association ambiguity produce diagnostics that include the location,
stage, raw value where safe, and remediation. They do not hide the asset or
abort the remaining scan. No metadata parser may execute shell text supplied by
a filename or tag.

Metadata inspection is read-only. Any future “write corrected metadata”
workflow is a separate, explicitly approved derivative/sidecar-or-original
editing feature, with backup, verification, journal and undo requirements. An
organization plan remains a dry run until approved and must show the exact
date source, override and precision behind every resulting folder or filename.
