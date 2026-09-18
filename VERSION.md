# Ollama Model Explorer Version

## Version 0.7.1

Date: 2026-09-18

### Corrections and improvements

- Model-information synchronization now checks the **complete model inventory**, not the currently filtered/displayed grid rows. Both Found and Missing models are included.
- The update form explicitly reports the complete model count and ignores active main-grid filters.
- The update action button changes from **Cancel** to **Close** when the background task finishes, including successful completion, cancellation, and failure.
- Added **increase font**, **decrease font**, and **Reset Font** controls to the Model Information form. The information text can be adjusted from 8 pt through 24 pt.
- Confirmed **Backup DB** and **Restore DB** controls are part of the Model Information form. Restore validates the selected database and creates an automatic pre-restore safety backup.
- Strengthened Windows application-icon configuration by explicitly assigning `OllamaIcon.ico` to both the application icon and Win32 icon metadata and retaining it through publish output.
- Kept the project configured for a future self-contained, single-file `win-x64` publish. No EXE is generated automatically.

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
