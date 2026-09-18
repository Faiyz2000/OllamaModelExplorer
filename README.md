# Ollama Model Explorer v0.7.0

## Major fixes and features

- Uses Ollama `http://localhost:11434/api/tags` as the authoritative installed-model inventory.
- Every model reported by Ollama is imported, including community/namespace models.
- `/api/show` enriches rows but can never cause a model to be dropped.
- Database identity is `Publisher + Name + Tag`, not a filesystem manifest path.
- The local inventory SQLite database remains independent from the model-information database.
- The UI refresh is asynchronous and remains responsive.
- DataGridView explicitly supports vertical and horizontal scrolling.
- Selecting the Ollama folder validates the storage root, then scans Ollama itself.
- Newly downloaded models appear after pressing Scan Local Models.

## Model information history

Version 0.7.0 adds a separate SQLite database for model information. It is independent from the local model inventory database and is intended to preserve information across PCs and clean Windows installations.

- The **Model Information** form reads preserved information and shows the current local **Found / Missing** status.
- Online information updates run in a separate non-modal progress window and do not block normal use of the main project.
- Only models currently displayed in the main grid are requested during the model-information update.
- Updates are staged in memory and committed only when the update form closes.
- Information is append-only. A successful update is added below previous information with an `Update on dd/MM/yy` separator. Existing information is never replaced by blank or missing website content.
- The **Backup DB** button creates a portable SQLite backup containing the complete information history.
- The **Restore DB** button validates and restores a previous information database, while automatically creating a pre-restore safety backup.
- The model-information database is not kept open during normal application use; connections are short-lived for required read/write/backup/restore operations.
- The information database is runtime-independent and does not require Ollama to execute a model.

## Installation status

The main grid includes a **Status** column showing `Found` when the model is present in the current configured local model inventory and `Missing` when its preserved database record exists but the model is not currently present. Missing models remain in the database so their information can be used when deciding what to download again on another PC.

## Model grid

The main grid includes a **RAM Required / Available** column. Each row displays the estimated RAM required to run that model followed by the PC's actual currently available physical RAM, in the format:

`Required RAM, Actual available RAM`

For example: `12.4 GB, 18.7 GB`.

The required RAM is a per-model estimate based on model size plus conservative runtime overhead. The actual available RAM is read directly from Windows and refreshed every second while the application is running, without rescanning the Ollama model inventory.

The **RAM Required / Available** column is sortable. Click its header to sort from lowest to highest estimated required RAM; click it again to reverse the order. Sorting uses the underlying numeric required-RAM estimate, not the displayed text, so the live available-RAM value does not affect the sort order. The header displays the active ascending/descending sort glyph, and RAM sorting remains active when filters rebuild the grid until another column is selected.

The project keeps `Services/RamEstimator.cs` as the calculation source and `Services/RamColumnFeature.cs` as the UI integration, live-memory, and sorting layer.

## Delete installed models

Version 0.6.5 includes an explicit **Delete Model** toolbar button. Select exactly one installed model, click **Delete Model**, and confirm the warning dialog. The application calls Ollama's local `http://localhost:11434/api/delete` endpoint and then performs a fresh scan. It never manually removes Ollama blobs/manifests.

## Online catalog

The **Check for New** action contacts Ollama.com only after explicit user approval. The model-information update is a separate operation and does not automatically download or replace catalog data.

The application expects the selected root to contain:
- `blobs`
- `manifests/registry.ollama.ai`

Ollama must be running at `http://localhost:11434` for local inventory scanning and model deletion.

## Build / publish

Target: .NET 8 Windows Forms, `win-x64`, self-contained, single-file publish. `OllamaIcon.ico` remains the application icon. The project uses an explicit compile list to avoid duplicate source/resource errors caused by stray copied `.cs` files.

The GitHub workflow currently performs a Release build only. Single-file self-contained EXE publishing is retained in the project configuration but is not automatically produced until requested.
