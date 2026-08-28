# Ollama Model Explorer Version

## Version 0.6.0

Date: 2026-08-28

### Changes in 0.6.0

- **Installed model discovery is now fresh-first.** The active grid is populated from Ollama's local `GET /api/tags` response, not from the SQLite database or the manifest directory.
- A fresh local scan runs automatically at application startup when the Ollama model root can be resolved from `OLLAMA_MODELS` or the standard Ollama location.
- **Scan Local Models** always obtains a new `/api/tags` result and replaces the grid's installed-model collection with that fresh result.
- SQLite is still maintained for persistence, but it is no longer authoritative for determining which models are installed. Cached database data is used only to preserve previously retrieved online metadata until the next online update.
- Local model records are synchronized into SQLite after every fresh scan.
- The application no longer allows stale manifest/database records to appear as installed models simply because they remain on disk.
- Duplicate Ollama tags are preserved as separate rows when `/api/tags` reports them, even if they reference the same underlying model digest.
- Added/confirmed the **RAM to Run** column as the final/right-most DataGridView column.
- RAM values remain dynamically calculated from current available physical RAM and the model's local size.
- Double-clicking a model now opens a dedicated model-details window containing a read-only, large-font text view with model identity, tag, size, modified time, parameters, family, quantization, format, context, RAM requirement, available RAM, categories, capabilities, installation state, metadata timestamp, Ollama URL, digest, manifest/API reference, and description.
- The model-details view uses locally available data and cached Ollama.com metadata; it does not require an Internet connection.
- **Update From Ollama.com** first refreshes the local installed-model list, then retrieves current public information for every locally installed model and updates the local online catalog cache.
- Online metadata remains cached locally between sessions and is changed only by an approved online update/check operation.
- **Check for New** remains an explicit, user-approved online operation and continues to populate the New on Ollama filtering state.
- Preserved existing filtering, size filters, comparison, logging, offline/local operation, parameter extraction, quantization extraction, and RAM estimation functionality.

### Data-source rules

1. **Installed models:** Ollama local API `/api/tags` — fresh on startup/scan.
2. **Detailed local model metadata:** Ollama local API `/api/show` — refreshed during local scan.
3. **Public Ollama.com metadata:** downloaded only after user approval and retained as local cache.
4. **SQLite:** persistence for metadata and synchronization only; never the authority for the current installed-model count.
5. **Ollama manifests:** not used as the installed-model list.

### Compatibility

- Existing SQLite database is retained.
- Existing Ollama model files are not deleted or modified.
- No model files, prompts, chats, local paths, or RAM information are uploaded by the application.
- Internet access is limited to explicit Ollama.com update/check actions.

### Version history

- `0.6.0` — fresh-first local synchronization, right-most RAM column, enhanced double-click model details, online metadata synchronization.
- `0.5.1` — RAM refresh safety fix.
- `0.5.0` — RAM estimation feature.

### Pre-update snapshots

Earlier snapshots are retained under the repository's `versions/` history where available.
