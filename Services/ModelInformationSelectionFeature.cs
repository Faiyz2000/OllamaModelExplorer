using System.Reflection;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Keeps the currently selected model available to the model-information form.
/// This does not change the existing double-click behavior.
/// </summary>
public static class ModelInformationSelectionFeature
{
    private static ModelInfo? _selectedModel;

    public static ModelInfo? SelectedModel => _selectedModel;

    public static void Attach(Form form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));
        var grid = FindControl<DataGridView>(form);
        if (grid is null) return;

        void UpdateSelection()
        {
            var row = grid.CurrentRow ?? grid.SelectedRows.Cast<DataGridViewRow>().FirstOrDefault();
            _selectedModel = TryGetModel(row);
        }

        grid.SelectionChanged += (_, _) => UpdateSelection();
        grid.CurrentCellChanged += (_, _) => UpdateSelection();
        UpdateSelection();
    }

    private static ModelInfo? TryGetModel(DataGridViewRow? row)
    {
        if (row?.DataBoundItem is null) return null;
        var property = row.DataBoundItem.GetType().GetProperty("Model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(row.DataBoundItem) as ModelInfo;
    }

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
