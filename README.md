# Ollama Model Explorer v0.6.7

## Major fixes and features

- Uses Ollama `http://localhost:11434/api/tags` as the authoritative installed-model inventory.
- Every model reported by Ollama is imported, including community/namespace models.
- `/api/show` enriches rows but can never cause a model to be dropped.
- Database identity is `Publisher + Name + Tag`, not a filesystem manifest path.
- The local SQLite database is disposable and rebuilt for a fresh inventory.
- The UI refresh is asynchronous and remains responsive.
- DataGridView explicitly supports vertical and horizontal scrolling.
- Selecting the Ollama folder validates the storage root, then scans Ollama itself.
- Newly downloaded models appear after pressing Scan Local Models.

## Model grid

The main grid includes a **RAM Required / Available** column. Each row displays the estimated RAM required to run that model followed by the PC's actual currently available physical RAM, in the format:

`Required RAM, Actual available RAM`

For example: `12.4 GB, 18.7 GB`.

The required RAM is a per-model estimate based on model size plus conservative runtime overhead. The actual available RAM is read directly from Windows and refreshed every second while the application is running, without rescanning the Ollama model inventory.

The **RAM Required / Available** column is sortable. Click its header to sort from lowest to highest estimated required RAM; click it again to reverse the order. Sorting uses the underlying numeric required-RAM estimate, not the displayed text, so the live available-RAM value does not affect the sort order. The header displays the active ascending/descending sort glyph, and RAM sorting remains active when filters rebuild the grid until another column is selected.

The project keeps `Services/RamEstimator.cs` as the calculation source and `Services/RamColumnFeature.cs` as the UI integration, live-memory, and sorting layer.

## Delete installed models

Version 0.6.5 includes an explicit **Delete Model** toolbar button. Select exactly one installed model, click **Delete Model**, and confirm the warning dialog. The application calls Ollama's local `http://localhost:11434/api/delete` endpoint and then performs a fresh scan. It never manually removes Ollama blobs/manifests.

## Online metadata

The **Update From Ollama.com** and **Check for New** actions are the operations that contact Ollama.com. Local model scanning and deletion use the local Ollama service.

The application expects the selected root to contain:
- `blobs`
- `manifests/registry.ollama.ai`

Ollama must be running at `http://localhost:11434`.

## Build / publish

Target: .NET 8 Windows Forms, `win-x64`, self-contained, single-file publish. The project uses an explicit compile list to avoid duplicate source/resource errors caused by stray copied `.cs` files.