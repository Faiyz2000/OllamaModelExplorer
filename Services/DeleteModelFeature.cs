using System.Reflection;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Adds the Delete Model action to the existing main form. Installed models are
/// removed through Ollama; missing models are removed from the local model catalog.
/// </summary>
public static class DeleteModelFeature
{
    public static void Attach(Form form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));
        if (form.Controls.OfType<Button>().Any(b => b.Name == "OllamaDeleteButton")) return;

        var grid = FindControl<DataGridView>(form);
        var toolbar = form.Controls.OfType<FlowLayoutPanel>().FirstOrDefault(p => p.Height >= 50);
        if (grid is null || toolbar is null) return;

        var button = new Button
        {
            Name = "OllamaDeleteButton",
            Text = "Delete Model",
            AutoSize = true,
            Enabled = false
        };

        toolbar.Controls.Add(button);
        grid.SelectionChanged += (_, _) => UpdateEnabled(button, grid);
        button.Click += async (_, _) => await DeleteSelectedAsync(form, grid, button);
        UpdateEnabled(button, grid);
    }

    private static void UpdateEnabled(Button button, DataGridView grid)
    {
        button.Enabled = grid.SelectedRows.Count == 1 && TryGetModel(grid.SelectedRows[0], out _);
    }

    private static async Task DeleteSelectedAsync(Form form, DataGridView grid, Button button)
    {
        if (grid.SelectedRows.Count != 1 || !TryGetModel(grid.SelectedRows[0], out var model))
            return;

        var action = model.Installed
            ? "Permanently delete the selected installed Ollama model?"
            : "Delete the selected missing model from the local model catalog?";
        var consequence = model.Installed
            ? "This removes the installed model from Ollama. To use it again, it must be downloaded/pulled again."
            : "The model is not installed on this PC. Its local catalog record, including its associated model information, will be removed from this project.";

        var answer = MessageBox.Show(
            form,
            $"{action}\r\n\r\n{model.DisplayName}\r\n\r\n{consequence}\r\n\r\nThis action cannot be undone from OllamaModelExplorer.",
            "Confirm model deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            AppLogger.Action($"Model deletion cancelled: {model.DisplayName}");
            return;
        }

        try
        {
            button.Enabled = false;
            var exactName = GetExactName(model);

            if (model.Installed)
            {
                AppLogger.Action($"Deleting installed Ollama model: {exactName}");
                await new OllamaScanner().DeleteModelAsync(model);
                AppLogger.Info($"Installed model deleted successfully: {exactName}");
            }
            else
            {
                AppLogger.Action($"Deleting missing model catalog record: {exactName}");
                var deleted = new Database().DeleteModelRecord(model);
                if (!deleted)
                    throw new InvalidOperationException("The missing model record could not be found in the local catalog.");
                AppLogger.Info($"Missing model catalog record deleted successfully: {exactName}");
            }

            var scan = form.GetType().GetMethod("ScanLocalAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            if (scan is not null)
            {
                var task = scan.Invoke(form, null) as Task;
                if (task is not null) await task;
            }

            MessageBox.Show(
                form,
                model.Installed
                    ? $"The installed model was deleted successfully.\r\n\r\n{model.DisplayName}"
                    : $"The missing model was removed from the local catalog successfully.\r\n\r\n{model.DisplayName}",
                "Model deleted",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Unable to delete model: {model.DisplayName}", ex);
            MessageBox.Show(form, "The model could not be deleted.\r\n\r\n" + ex.Message, "Delete error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateEnabled(button, grid);
        }
    }

    private static bool TryGetModel(DataGridViewRow row, out ModelInfo model)
    {
        model = null!;
        var item = row.DataBoundItem;
        if (item is null) return false;

        var property = item.GetType().GetProperty("Model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(item) is ModelInfo value)
        {
            model = value;
            return true;
        }
        return false;
    }

    private static string GetExactName(ModelInfo model) =>
        model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
            ? $"{model.Name}:{model.Tag}"
            : $"{model.Publisher}/{model.Name}:{model.Tag}";

    private static T? FindControl<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) return match;
            var nested = FindControl<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}