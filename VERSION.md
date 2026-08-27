# Ollama Model Explorer Version

## Version 0.5.0

Date: 2026-08-27

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

### Pre-update snapshot

A Git branch named `versions/v0.5.0-pre-ram-estimation` was created from `main` before the 0.5.0 changes. It is the rollback/snapshot copy for this version.
