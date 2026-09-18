using System.Reflection;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Adds the Delete Model action to the existing main form. Supports one or more
/// selected rows. Installed models are removed through Ollama; missing models are
/// removed from the local model catalog.
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
        button.Enabled = GetSelectedModels(grid).Count > 0;
    }

    private static List<ModelInfo> GetSelectedModels(DataGridView grid)
    {
        return grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => TryGetModel(row, out var model) ? model : null)
            .Where(model => model is not null)
            .Select(model => model!)
            .GroupBy(model => GetIdentity(model), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task DeleteSelectedAsync(Form form, DataGridView grid, Button button)
    {
        var models = GetSelectedModels(grid);
        if (models.Count == 0) return;

        var installed = models.Where(m => m.Installed).ToList();
        var missing = models.Where(m => !m.Installed).ToList();

        var actionSummary = new List<string>();
        if (installed.Count > 0)
            actionSummary.Add($"Installed models to delete from Ollama: {installed.Count}");
        if (missing.Count > 0)
            actionSummary.Add($"Missing models to remove from the local catalog: {missing.Count}");

        var names = string.Join(Environment.NewLine, models.Select(m => $"• {m.DisplayName}"));
        var consequence = "\r\n\r\nThis action cannot be undone from OllamaModelExplorer.";
        if (missing.Count > 0)
            consequence += "\r\nPreserved model-information history is not deleted by removing a missing model from the local catalog.";

        var answer = MessageBox.Show(
            form,
            $"Delete the {models.Count} selected model(s)?\r\n\r\n" +
            string.Join("\r\n", actionSummary) +
            $"\r\n\r\n{names}{consequence}",
            "Confirm model deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
        {
            AppLogger.Action($"Model deletion cancelled for {models.Count} selected model(s).");
            return;
        }

        try
        {
            button.Enabled = false;
            var deletedCount = 0;
            var failed = new List<string>();

            // Process sequentially so the local Ollama service is not flooded by
            // simultaneous delete requests when many models are selected.
            foreach (var model in models)
            {
                var exactName = GetExactName(model);
                try
                {
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

                    deletedCount++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{model.DisplayName}: {ex.Message}");
                    AppLogger.Error($"Unable to delete model: {exactName}", ex);
                }
            }

            var scan = form.GetType().GetMethod("ScanLocalAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            if (scan is not null && deletedCount > 0)
            {
                var task = scan.Invoke(form, null) as Task;
                if (task is not null) await task;
            }

            if (failed.Count == 0)
            {
                MessageBox.Show(
                    form,
                    $"{deletedCount} selected model(s) were deleted successfully.",
                    "Models deleted",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                var failureText = string.Join(Environment.NewLine, failed.Take(10));
                if (failed.Count > 10)
                    failureText += Environment.NewLine + $"...and {failed.Count - 10} more failure(s).";

                MessageBox.Show(
                    form,
                    $"Completed deletion for {deletedCount} of {models.Count} selected model(s).\r\n\r\nFailed:\r\n{failureText}",
                    "Deletion completed with errors",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Unable to complete multi-model deletion.", ex);
            MessageBox.Show(form, "The selected models could not be deleted.\r\n\r\n" + ex.Message, "Delete error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private static string GetIdentity(ModelInfo model) =>
        $"{model.Publisher}/{model.Name}:{model.Tag}";

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
