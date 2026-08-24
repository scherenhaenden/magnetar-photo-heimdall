# Heimdall feature catalogue

This is the product specification extracted from the two legacy sources:

- `../Photos`, a collection of organizer, gallery, preview, image-processing,
  metadata, backup and duplicate-detection experiments;
- `../Photos/PhotoLibrarizer`, the focused library-organizer CLI prototype.

The source projects are evidence, not dependencies. Several are drafts, have
empty UI shells, or execute destructive operations without confirmation.
Heimdall must provide the useful user outcome with one coherent desktop
experience and the safety guarantees documented below.

| User-facing capability | Specification |
| --- | --- |
| Configure libraries | [01-library-roots.md](01-library-roots.md) |
| Discover and catalogue media | [02-scan-and-catalogue.md](02-scan-and-catalogue.md) |
| Inspect metadata and dates | [03-metadata-and-dates.md](03-metadata-and-dates.md) |
| Browse and find assets | [04-library-browser.md](04-library-browser.md) |
| Identify duplicate candidates | [05-duplicate-review.md](05-duplicate-review.md) |
| Organize files by date and rules | [06-organization-plan.md](06-organization-plan.md) |
| Resolve naming collisions | [07-naming-and-collisions.md](07-naming-and-collisions.md) |
| Make previews and RAW derivatives | [08-previews-and-raw.md](08-previews-and-raw.md) |
| Export resized/watermarked copies | [09-export-watermark.md](09-export-watermark.md) |
| Back up and mirror libraries | [10-backup-and-mirrors.md](10-backup-and-mirrors.md) |
| Diagnose and sanitize libraries | [11-library-health.md](11-library-health.md) |
| Adapt work to machine temperature | [12-thermal-workload-control.md](12-thermal-workload-control.md) |

## Non-negotiable product rules

Every potentially changing operation is first a visible plan. A user chooses
which operations to approve; Heimdall copies, verifies content, places an old
source into quarantine, records an immutable journal entry, and provides undo.
No first-release feature may permanently delete an original. This deliberately
replaces legacy calls such as direct `File.Delete`, shell `ln`, and unquoted
external commands.

For each view, Heimdall must distinguish **available now**, **proposed**,
**running**, **completed**, **failed**, and **undoable**. Errors must retain the
asset path and the step that failed; they must never disappear in a silent
catch block as they do in some prototypes.
