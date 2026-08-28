# Ollama Model Explorer Version

## Version 0.6.1

Date: 2026-08-29

### Changes in 0.6.1

- Fixed the project-build failure caused by duplicate source/resource files being automatically included by the .NET SDK.
- Changed the project to use an explicit compile list for the repository's intended `.cs` files.
- This prevents stray copies, backup files, or duplicate `LogForm`/`AppLogger` source files in the project directory from creating C# ambiguity errors.
- This also prevents duplicate WinForms resource generation such as `OllamaModelExplorer.UI.LogForm.resources` when unrelated duplicate source files are present locally.
- Retained the existing Windows Forms application, SQLite support, model scanning, live Ollama inventory, metadata/quantization handling, RAM estimation, logging, online update controls, and other existing functionality.
- Retained the Windows x64 self-contained/single-file publish settings in the project file.
- Updated application version to 0.6.1.

### Important build note

The repository itself contains only the intended source files. The explicit compile list makes the build independent of extra `.cs` files that may be left in a downloaded/copy-pasted project directory. After updating, delete `bin` and `obj` once and perform a Restore/Rebuild before publishing.

### Pre-update snapshot

- `versions/v0.6.0-pre-build-fix` — branch snapshot created before the 0.6.1 build-fix update.

## Version 0.5.1

Date: 2026-08-27

### Changes in 0.5.1

- Fixed safe refresh/invalidation of the RAM column when available system memory changes.
- RAM status is refreshed every 5 seconds without rebuilding the model grid or altering model records.
- Preserved all 0.5.0 RAM estimation functionality.

## Version 0.5.0

### Changes

- Added a new **RAM to Run** column to the right of Quantization in the model grid.
- The RAM estimate is calculated from each model's local stored size plus a conservative runtime overhead allowance of approximately 15% and a minimum 512 MiB.
- The application reads the Windows **currently available physical RAM** using `GlobalMemoryStatusEx`; it does not assume that a fixed amount such as 8 GB is always available.
- Each RAM cell reports an approximate requirement, current available RAM, and a simple `OK` / `NOT ENOUGH` assessment.
- RAM availability is refreshed every 5 seconds while the application is running, so the assessment changes as available memory changes.
- RAM information is also shown in the model details and model comparison views.
- RAM estimation is local-only and does not send memory information or model information to the Internet.
- Existing model discovery, Ollama metadata extraction, filters, comparison, logging, offline behavior, and other features are preserved.

### RAM estimate methodology

The estimate is intentionally approximate. On-disk model size is not identical to peak runtime memory because Ollama/model-runtime allocations, loader overhead, and context/KV-cache memory can add to the requirement. The application therefore uses:

`Estimated RAM = model size + max(15% of model size, 512 MiB)`

The result should be treated as a planning indicator, not a guarantee of exact peak memory usage.

### Compatibility

- Existing SQLite database is retained.
- Existing model files are not deleted or modified.
- No Internet connection is required for RAM estimation.

### Pre-update snapshots

- `versions/v0.5.0-pre-ram-estimation` — snapshot created before the 0.5.0 RAM feature.
- `versions/v0.5.0-pre-ram-refresh-fix` — snapshot created before the 0.5.1 RAM refresh safety fix.
