# Ollama Model Explorer Version

## Version 0.6.4

Date: 2026-09-02

### Changes in 0.6.4

- Added a **Delete** button to the main model toolbar.
- The Delete button is enabled only when exactly one installed model is selected.
- Added an explicit confirmation dialog before deletion; choosing No/cancel performs no deletion.
- Deletion uses Ollama's local `http://localhost:11434/api/delete` endpoint, so model removal is performed by Ollama rather than by directly manipulating blobs/manifests.
- After successful deletion, the application performs a fresh local model scan so the DataGrid reflects the current Ollama inventory.
- Added logging for deletion requests, cancellation, success, and errors.
- Preserved all existing scanning, metadata, RAM estimation, online catalog, comparison, details, logging, filtering, and database functionality.

### Pre-update snapshot

- `versions/v0.6.2-pre-delete-button` — repository snapshot created before the 0.6.4 Delete-button update.

## Version 0.6.2

Date: 2026-08-29

### Changes in 0.6.2

- Configured the Windows application executable to use `OllamaIcon.ico` through the project `ApplicationIcon` property.
- Added the icon as project content so it is included by the project when the icon file is present beside the `.csproj` file.
- Added `BUTTONS.md`, a detailed reference for every main button and filter/control, including Internet access behavior, local Ollama API behavior, database effects, and model-information refresh behavior.
- Documented the distinction between local model scanning and online Ollama catalog enrichment.
- Preserved all v0.6.1 functionality.

### Important icon note

The supplied `OllamaIcon.ico` is a binary file. The available repository file-writing interface can update UTF-8 source/documentation files but cannot directly upload binary files. Therefore the project is configured to use the filename `OllamaIcon.ico`; the supplied icon must be copied into the repository/project root beside `OllamaModelExplorer.csproj` before building/publishing. If the file is absent, the icon cannot be embedded into the executable.

### Pre-update snapshot

- `versions/v0.6.1-pre-icon-and-button-documentation` — repository snapshot created before the 0.6.2 update.

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