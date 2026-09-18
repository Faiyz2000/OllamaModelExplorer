# Ollama Model Explorer Version

## Version 0.7.7

Date: 2026-09-19

### Exact Ollama model information snapshots

- The Model Information Update operation now fetches the **specific Ollama model page for the selected model and tag**, for example `nemotron-3.5-lightning:30b` uses the corresponding tagged model page rather than a generic catalog description.
- The captured page is preserved as an offline HTML snapshot in the separate model-information SQLite database.
- Referenced page images, including benchmark images when present, are downloaded during the explicit update operation and embedded into the offline snapshot so they remain available without Internet access.
- Previous snapshots are never overwritten. A changed page creates another historical snapshot, separated in the Model Information form by the capture date.
- Blank, missing, failed, or deleted Ollama.com pages cannot erase previously preserved information.
- The Model Information form displays the preserved page snapshot offline and retains the existing font-size controls.
- Backup and Restore include the complete offline page snapshots because they are stored inside the SQLite database.
- The existing **double-click a model row** workflow is unchanged.

### Version 0.7.5 online-access policy retained

- The explicit **Update Model Information** action is the only user action permitted to access the Internet / Ollama.com.
- Local communication with the Ollama service remains permitted for normal local model operations, including scanning installed models and deleting installed models.
- The previous **Check for New** action is disabled so it cannot perform an online request outside the approved information-update workflow.
- Local model information, filtering, sorting, comparison, database backup/restore, and other local operations do not access the Internet.

### Version 0.7.4 features retained

- **Delete Model** supports multiple selected rows, including mixed Found and Missing models.
- Installed models are deleted through the local Ollama service; Missing models are removed from the local model catalog.
- Deletions are processed sequentially and failures do not prevent remaining selected models from being attempted.

### Version 0.7.2 features retained

- Model-information synchronization always processes the complete model inventory, regardless of active grid filters, search, category, size, or Installed/Enriched/New filters. Found and Missing models are both included.
- The update confirmation explicitly reports that grid filters are ignored.
- The non-modal update form changes its action from **Cancel** to **Close** when the background task finishes, including successful completion, cancellation, and failure.
- The existing double-click row workflow for opening model information is retained.
- Model Information font controls: increase, decrease, and Reset Font, with an 8–24 pt range.
- Backup DB and Restore DB controls, including automatic pre-restore safety backup.
- Windows executable icon configuration using `OllamaIcon.ico` as both `ApplicationIcon` and `Win32Icon`, with the icon copied to publish output.
- Self-contained, single-file `win-x64` publish configuration. No EXE is generated automatically.

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
- Online catalog cache and existing local filters remain available without network access; online refresh is restricted to the explicit Model Information Update action.
- Search, filtering, comparison, logging, and existing UI workflow.
