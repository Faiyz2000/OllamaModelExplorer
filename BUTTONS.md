# Ollama Model Explorer — Button and Control Reference

Version 0.7.0

This document describes what each button/control does, whether it accesses the Internet, and what model information it changes.

## 1. Select Ollama Folder

**Purpose:** Select the root of the local Ollama model storage.

**Typical folder:** `D:\OllamaModels`

The selected root is expected to contain `blobs` and `manifests\registry.ollama.ai`.

**Internet:** No. The folder scan itself does not require an Internet connection. Ollama must be running locally at `http://localhost:11434`.

**Database:** The local inventory database is updated by the scan. The separate model-information database is not required for ordinary scanning.

---

## 2. Scan Local Models

**Purpose:** Force a fresh synchronization of locally installed models.

**Internet:** No external Internet connection is required. It communicates with the local Ollama service at `http://localhost:11434` and reads the selected storage directory.

**What it updates:** Installed model inventory, model names/tags, model IDs, local model sizes, locally obtainable metadata, and installed status.

**Important:** This is the button to use after downloading additional models. It refreshes the local inventory and the Found/Missing state.

---

## 3. Update Model Information

**Purpose:** Retrieve current public model information for the models currently displayed in the main grid.

**Internet:** **Yes.** This is an explicit, user-started online operation. Normal project use does not contact Ollama.com for this feature.

**Behavior:** A separate non-modal progress form performs asynchronous requests. The main project remains usable while the update runs.

**Important:**
- Only models currently displayed in the grid are requested.
- Existing model information is preserved.
- New information is staged in memory while the update form is open.
- Database changes are committed only when the update form closes.
- Information is append-only; successful updates are added below previous information with `Update on dd/MM/yy`.
- Blank, unavailable, deleted, or failed website information cannot erase existing history.
- The operation does not download model weights and does not run models.

---

## 4. Model Information

**Purpose:** View preserved model information independently of the currently installed model files.

The form shows whether each model is currently `Found` or `Missing` and displays its preserved information history.

**Internet:** No. Opening the form does not contact Ollama.com.

**Database:** The separate model-information SQLite database is accessed through short-lived connections and is not held open during normal use.

**Backup DB:** Creates a portable SQLite backup containing the complete model-information history.

**Restore DB:** Validates and restores a previous model-information database. An automatic pre-restore safety backup is created first.

---

## 5. Check for New

**Purpose:** Identify models that are available in the Ollama catalog but are not currently represented in the local catalog state.

**Internet:** Yes, after explicit user approval.

**Important distinction:** This is not a model download operation and does not install model weights.

---

## 6. Installation Status column

The main grid includes a **Status** column showing:

- `Found` — the model is present in the current local inventory.
- `Missing` — the model is preserved in the inventory/database but is not currently present locally.

The status does not determine whether the Ollama website still contains the model. Website availability and local installation status are separate concepts.

---

## 7. Delete

**Purpose:** Permanently remove the selected installed model from Ollama.

**Selection requirement:** Exactly one installed model must be selected. The application calls Ollama's local `/api/delete` endpoint and then performs a fresh scan.

**Internet:** No.

**Database:** The normal local scan refreshes the installed state. Preserved model information is not deleted merely because the model is missing locally.

---

## 8. Compare Selected

**Purpose:** Compare multiple selected model records side by side.

**Internet:** No.

---

## 9. View Log

**Purpose:** Open the application's activity log viewer.

**Internet:** No.

---

## 10. Search / Category / Size / Installed / Enriched / New on Ollama filters

These controls only change which records are displayed. They do not contact the Internet or delete model information.

The **Installed** filter can be cleared when the user wants to see both `Found` and `Missing` models.

---

## 11. DataGridView column headers

Clicking a column header sorts the displayed rows by that column. Clicking the same header again reverses the sort direction. Sorting does not modify model files or model information.
