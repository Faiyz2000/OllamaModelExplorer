# Ollama Model Explorer v0.6.0

A Windows Forms application for viewing and managing locally installed Ollama models.

## Model source of truth

The installed-model grid is **fresh-first**:

- On startup, the application attempts an automatic local scan.
- `Scan Local Models` always queries Ollama at `http://localhost:11434/api/tags`.
- The result of `/api/tags` determines which models are installed and which rows appear in the grid.
- `/api/show` is then used to enrich each current model with parameters, quantization, family, format, context and capabilities where available.
- The filesystem manifest directory is **not** used as the installed-model inventory.
- SQLite is refreshed after every local scan but is not authoritative for the current installed-model list.
- Duplicate Ollama tags are retained as separate rows when Ollama reports them.

## Online metadata

`Update From Ollama.com` is explicitly user-approved and performs two operations:

1. Refreshes the current local installed-model list from Ollama.
2. Retrieves public Ollama.com information for every locally installed model and refreshes the public catalog/new-model information.

Online metadata is retained locally so it can be viewed offline between approved updates. No model files, prompts, chats, local paths, or RAM information are uploaded.

`Check for New` remains an explicit online operation for checking the public newest-model catalog.

## UI features

- Model name, publisher, tag, size, parameters, family and quantization.
- Category and size filters.
- Search and installed/enriched/new-model filters.
- RAM-to-run estimate as the **right-most** grid column.
- Live available-RAM refresh.
- Double-click any model to open a large-font, read-only model-details window.
- Model comparison.
- Local application log with `View Log`.
- Vertical and horizontal DataGridView scrolling.
- Offline operation except for explicitly approved Ollama.com operations.

## Storage requirements

The selected Ollama root must contain:

- `blobs`
- `manifests/registry.ollama.ai`

The application requires Ollama to be running at `http://localhost:11434` for local model discovery.

## Version

See `VERSION.md` for the detailed 0.6.0 change history.
