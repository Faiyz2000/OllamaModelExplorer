# Ollama Model Explorer Version

## Version 0.7.0

Date: 2026-09-18

### Changes in 0.7.0

- Added a separate SQLite model-information database; it is independent from the local model inventory database.
- Added a dedicated **Model Information** form that displays preserved information history.
- Model information is append-only: successful online updates are added as dated snapshots and never overwrite or delete earlier information.
- A missing, deleted, blank, or failed Ollama.com page cannot erase previously preserved model information.
- Added a non-modal **Model Information Update** form with asynchronous background fetching and progress reporting; the main project remains usable during updates.
- Only models currently displayed in the main model grid are sent to the online information update process.
- New information is staged in memory and committed only when the update form closes.
- Added **Backup DB** and **Restore DB** to the Model Information form.
- Restore validates the database and creates an automatic pre-restore safety backup.
- Added an explicit **Found / Missing** installation-status column to the main model grid. Status reflects the current local model inventory and does not delete information for missing models.
- Preserved runtime independence: model information is about models, not a dependency on Ollama as the runtime used to execute them.
- Retained `OllamaIcon.ico` as the application/EXE icon.
- Kept the project self-contained and single-file publish configuration for `win-x64`.

### Existing functionality retained

- Live RAM Required / Available column and numeric RAM sorting.
- Local model scanning and deletion through the local Ollama service.
- Online catalog and Check for New functionality.
- Search, filtering, comparison, logging, and existing UI workflow.

## Version 0.6.7

Date: 2026-09-03

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
- RAM sorting remains active when filters rebuild the grid until another column is selected for sorting.

## Version 0.6.5

Date: 2026-09-03

- Restored the **RAM Required** column to the main model grid.
- RAM Required is a per-model estimate based on model size plus conservative runtime overhead; it is not the PC's current free RAM.
- Added `Services/RamColumnFeature.cs` and enabled it from `Program.cs`.
- Restored and explicitly labeled the **Delete Model** toolbar button.
- Preserved the Ollama local delete API workflow, confirmation, logging, and post-delete rescan.
