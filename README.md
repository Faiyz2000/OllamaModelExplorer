# Ollama Model Explorer v5

## Major fixes

- Uses Ollama `http://localhost:11434/api/tags` as the authoritative installed-model inventory.
- Every model reported by Ollama is imported, including community/namespace models.
- `/api/show` enriches rows but can never cause a model to be dropped.
- Database identity is `Publisher + Name + Tag`, not a filesystem manifest path.
- Existing legacy SQLite schema is migrated and the old singular `Model` table is removed.
- The UI refresh is asynchronous and remains responsive.
- DataGridView explicitly supports vertical scrolling.
- Selecting the Ollama folder validates the storage root, then scans Ollama itself.
- Newly downloaded models appear after pressing Scan Local Models.

The application expects the selected root to contain:
- `blobs`
- `manifests/registry.ollama.ai`

Ollama must be running at `http://localhost:11434`.
