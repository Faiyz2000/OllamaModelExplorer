# Ollama Model Explorer — Button and Control Reference

Version 0.6.2

This document describes what each button/control does, whether it accesses the Internet, and what model information it changes.

## 1. Select Ollama Folder

**Purpose:** Select the root of the local Ollama model storage.

**Typical folder:** `D:\OllamaModels`

The selected root is expected to contain `blobs` and `manifests\registry.ollama.ai`.

**What happens:** The application immediately starts the local model scan after the folder is selected. The scanner uses Ollama's local API as the authoritative installed-model inventory and uses the selected storage root for local storage/model information.

**Internet:** No. The folder scan itself does not require an Internet connection. Ollama must be running locally at `http://localhost:11434`.

**Database:** The scan updates the local SQLite records. It does not delete the model files. The current Ollama inventory is imported/updated so newly downloaded models can appear.

---

## 2. Scan Local Models

**Purpose:** Force a fresh synchronization of locally installed models.

**Internet:** No external Internet connection is required. It communicates with the local Ollama service at `http://localhost:11434` and reads the selected local storage directory.

**What it updates:**
- Installed model inventory
- Model names/tags
- Model IDs
- Local model sizes
- Parameters when available from Ollama metadata
- Model family information
- Quantization when available
- Other locally obtainable metadata
- Installed status
- Local database records used by the UI

**Important:** This is the button to use after downloading additional models. It should refresh the local inventory rather than relying on an old model count.

**Does it contact Ollama.com?** No. It is a local synchronization operation.

---

## 3. Update From Ollama.com

**Purpose:** Enrich the local model records with information from the public Ollama catalog and synchronize public catalog information.

**Internet:** **Yes.** This is the primary Internet-connected operation in the application.

**What it is intended to update:**
- Public Ollama model/catalog information
- Descriptions
- Public model metadata
- Information associated with models that exist in the Ollama online catalog
- Local records with newly retrieved public information
- Detection information used by the `New on Ollama` filter

**What it does NOT do:**
- It does not download model weights.
- It does not start or run models.
- It does not delete local models.
- It does not replace the local scan.
- It does not change the actual Ollama model files.

**Recommended use:** Run `Scan Local Models` when you have changed local models. Run `Update From Ollama.com` when you want current public catalog information or want to check the local inventory against the online Ollama catalog.

---

## 4. Check for New

**Purpose:** Identify models that are available in the Ollama catalog but are not currently represented in the local model inventory/catalog state.

**Internet:** The check is associated with the online Ollama catalog and therefore requires Internet access when the online catalog is queried.

**Important distinction:** `Check for New` is not the same as downloading a model. It only identifies catalog/new-model information; it does not install model weights.

The `New on Ollama` filter can then be used to display records marked as new on the Ollama catalog.

---

## 5. Compare Selected

**Purpose:** Compare multiple selected model records side by side.

**Internet:** No. Comparison uses information already available in the application/database.

**Information compared can include:**
- Model name
- Size
- Parameters
- Family
- Quantization
- RAM-to-run estimate
- Categories
- Other model metadata displayed by the application

**Does it update models?** No. It is a viewing/analysis function.

---

## 6. View Log

**Purpose:** Open the application's activity log viewer.

**Internet:** No.

The log records application actions and operational messages such as application startup, folder selection, local scans, update/check operations, errors, and other significant actions.

The log window provides:
- Refresh
- Open in Notepad
- Clear Current Log

`Open in Notepad` launches Windows Notepad with the current log file. It does not send the log anywhere.

---

## 7. Search box

**Purpose:** Filter the displayed model list.

It searches model/display name, publisher, and description.

**Internet:** No.

**Model data changed:** No. Search only changes which records are displayed.

---

## 8. Category filter

**Purpose:** Show models belonging to a selected category.

**Internet:** No.

**Model data changed:** No. This is a display filter.

---

## 9. Size filter

**Purpose:** Filter models by local stored size ranges.

**Internet:** No.

**Model data changed:** No.

---

## 10. Installed filter

**Purpose:** Show only models currently marked as locally installed.

**Internet:** No.

The installed state originates from the local Ollama synchronization/scan.

---

## 11. Enriched filter

**Purpose:** Show models for which Ollama/public metadata enrichment has been recorded.

**Internet:** No when merely filtering. The filter itself does not perform an online lookup.

---

## 12. New on Ollama filter

**Purpose:** Show models marked as new/available through the Ollama online catalog comparison.

**Internet:** No when merely filtering. Use the online update/check functions to obtain current catalog information.

---

## 13. DataGridView column headers

Clicking a column header sorts the displayed rows by that column. Clicking the same header again reverses the sort direction.

**Internet:** No.

Sorting does not modify model files or model records.

---

## 14. Double-click a model

**Purpose:** Open the detailed model information form for the selected model.

This is a viewing operation and does not start the model.

The detail view can contain information such as the model name, publisher, tag, size, parameters, family, quantization, RAM-to-run estimate, description, categories, and other stored/enriched metadata depending on what is available for that model.

**Internet:** Opening the details window itself does not inherently require Internet access. It displays information already stored by the application. Online information is refreshed by the explicit online update operation rather than by simply opening a model.

---

## 15. RAM to Run column

This is not the computer's currently available RAM.

It is an **estimated amount of RAM required to run the individual model**.

The current implementation uses the local model size plus a conservative runtime allowance. The estimate is approximately:

`Estimated RAM = model size + max(15% of model size, 512 MiB)`

It is a planning estimate, not an exact measurement of peak runtime consumption. Context/KV-cache settings, GPU offloading, runtime behavior, and other factors can change actual memory usage.

**Internet:** No.

**Physical PC RAM:** The application may display current available RAM separately for status/assessment, but that does not change the model's calculated requirement.

---

## Recommended workflow

### After downloading a new model

1. Keep Ollama running.
2. Press **Scan Local Models**.
3. Confirm the new model appears in the grid.
4. If you want current public descriptions/catalog information, press **Update From Ollama.com**.

### For an offline/local-only session

Use:
- Select Ollama Folder
- Scan Local Models
- Search/filter controls
- Compare Selected
- View Log
- Double-click model details

These operations do not require the public Internet.

### When you want current Ollama catalog information

Use:
- **Update From Ollama.com** for public metadata/catalog enrichment.
- **Check for New** for online catalog/new-model checking.

These are the functions that should be treated as Internet-dependent.

## Data/database model

The SQLite database is application-side storage for model records and enriched metadata. It is not the authoritative source for determining what Ollama currently has installed.

The local Ollama API is the authoritative installed-model inventory, while scanning/synchronization refreshes the database records used to present the grid efficiently. Consequently, the database can contain historical/catalog records while the installed flag and current local inventory are synchronized from Ollama.

Model files themselves remain under the Ollama storage directory and are not copied into SQLite.
