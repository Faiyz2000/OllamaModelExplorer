# Ollama Model Explorer Version

## Version 0.7.2

Date: 2026-09-18

### Corrections and improvements

- Model-information synchronization now always processes the complete model inventory, regardless of active grid filters, search, category, size, or Installed/Enriched/New filters. Found and Missing models are both included.
- The update confirmation explicitly reports that grid filters are ignored.
- The non-modal update form changes its action from **Cancel** to **Close** when the background task finishes, including successful completion, cancellation, and failure.
- The existing double-click row workflow for opening model information is retained. No different grid interaction is introduced without notice.
- Model Information font controls are retained: increase, decrease, and Reset Font, with an 8–24 pt range.
- Backup DB and Restore DB controls are retained, including automatic pre-restore safety backup.
- Windows executable icon configuration is retained using `OllamaIcon.ico` as both `ApplicationIcon` and `Win32Icon`, and the icon is copied to publish output.
- The project remains configured for a self-contained, single-file `win-x64` publish. No EXE is generated automatically.

### Version 0.7.0 features retained

- Separate SQLite model-information database; independent from the local model inventory database.
- Dedicated **Model Information** form with preserved information history.
- Append-only model-information snapshots; successful online updates never overwrite or delete earlier information.
- Missing, deleted, blank, or failed Ollama.com information cannot erase previously preserved information.
- Non-modal **Model Information Update** form with asynchronous background fetching and progress reporting; the main project remains usable during updates.
- New information is staged in memory and committed only when the update form closes.
- **Backup DB** and **Restore DB** with automatic pre-restore safety backup.
- Explicit **Found / Missing** installation-status column in the main model grid.
- Missing models remain represented in the model-information database.
- Runtime-independent model information design; the project does not require Ollama as the runtime used to execute a model.
- Existing project UI and workflow retained.

### Existing functionality retained

- Live RAM Required / Available column and numeric RAM sorting.
- Local model scanning and deletion through the local Ollama service.
- Online catalog and Check for New functionality.
- Search, filtering, comparison, logging, and existing UI workflow.
