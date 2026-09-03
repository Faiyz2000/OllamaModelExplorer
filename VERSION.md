# Ollama Model Explorer Version

## Version 0.6.7

Date: 2026-09-03

### Changes in 0.6.7

- Extended the **RAM Required / Available** column to show both values in the format `Required RAM, Actual available RAM`.
- Required RAM remains the per-model estimate used for determining the model's approximate runtime memory requirement.
- Actual available RAM is read directly from Windows physical-memory status using `GlobalMemoryStatusEx`.
- The actual available RAM value is refreshed live every second while the application is running; the model inventory is not rescanned for this refresh.
- RAM sorting remains numeric by required RAM, independent of the live available-RAM display.
- Preserved all existing scanning, metadata, online catalog, comparison, details, logging, filtering, deletion, and disposable local database behavior.

### Pre-update snapshot

- `v0.6.6-pre-ram-sort` — Git branch snapshot created before the live RAM update.

## Version 0.6.6

Date: 2026-09-03

- Enabled sorting for the **RAM Required** column.
- RAM Required now sorts numerically by the estimated RAM requirement rather than alphabetically by its displayed text.
- Clicking the RAM Required header toggles between ascending and descending order.
- Added the ascending/descending sort glyph to the RAM Required header.
- RAM sorting remains active when filters rebuild the model grid until another column is selected for sorting.

## Version 0.6.5

Date: 2026-09-03

- Restored the **RAM Required** column to the main model grid.
- RAM Required is a per-model estimate based on model size plus conservative runtime overhead; it is not the PC's current free RAM.
- Added `Services/RamColumnFeature.cs` and enabled it from `Program.cs`.
- Restored and explicitly labeled the **Delete Model** toolbar button.
- Preserved the Ollama local delete API workflow, confirmation, logging, and post-delete rescan.

## Version 0.6.4

Date: 2026-09-02

- Added the Delete model workflow using Ollama's local `/api/delete` endpoint.
- Added confirmation, logging, and a fresh scan after successful deletion.
- Preserved existing functionality.

## Version 0.6.2

Date: 2026-08-29

- Configured the Windows application executable to use `OllamaIcon.ico`.
- Added `BUTTONS.md` documenting controls and their local/online behavior.

## Version 0.6.1

Date: 2026-08-29

- Fixed duplicate source/resource compilation problems by using an explicit compile list.
- Retained SQLite, Ollama scanning, metadata handling, RAM estimation, logging, online updates, and self-contained single-file publishing.

## Version 0.6.0 and earlier

Previous releases established the Ollama model inventory, metadata enrichment, filtering, comparison, logging, online catalog, and Windows single-file publishing foundations.