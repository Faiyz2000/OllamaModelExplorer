# Ollama Model Explorer Version

## Version 0.4.0

Date: 2026-08-27

### Changes

- Fixed Ollama metadata extraction from `/api/show`.
- Parameters now use Ollama `details.parameter_size` with additional fallbacks to parameter-count metadata.
- Quantization now uses Ollama `details.quantization_level` with file-type and model-name fallbacks.
- Added persistent local application logging.
- Added **View Log** button.
- Added a dedicated log viewer with refresh, clear-current-log, and Windows Notepad opening options.
- Added action, scan, metadata, error, and progress-related log entries.
- Log files remain on the local PC under `%LOCALAPPDATA%\\OllamaModelExplorer\\logs`.
- Added horizontal DataGridView scrolling while retaining vertical scrolling.
- Added **Check for New** button to make the intended online operation explicit without performing network access automatically.
- Set application assembly/file version to `0.4.0.0`.

### Compatibility

- Existing SQLite database is retained.
- Existing model discovery, filters, comparison, local scanning, and offline behavior are preserved.
- No model files are deleted or modified by this update.

### Pre-update snapshot

A Git branch named `versions/v0.4.0-pre-metadata-log` was created from the previous `main` commit before this update. It is the rollback/snapshot copy for this version.
