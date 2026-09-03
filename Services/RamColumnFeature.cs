using System.Reflection;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Adds the per-model RAM requirement column to the existing main model grid.
/// The value is an estimate of RAM required to load the model, not current free system RAM.
/// </summary>
public static class RamColumnFeature
{
    public static void Attach(Form form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));

        var grid = FindControl<DataGridView>(form);
        if (grid is null || grid.Columns.Cast<DataGridViewColumn>().Any(c => c.Name == "RamRequiredColumn"))
            return;

        var column = new DataGridViewTextBoxColumn
        {
            Name = "RamRequiredColumn",
            HeaderText = "RAM Required",
            Width = 125,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };

        var insertAt = Math.Min(3, grid.Columns.Count);
        grid.Columns.Insert(insertAt, column);
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != column.Index) return;
            if (grid.Rows[e.RowIndex].DataBoundItem is null) return;

            var property = grid.Rows[e.RowIndex].DataBoundItem.GetType()
                .GetProperty("Model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(grid.Rows[e.RowIndex].DataBoundItem) is ModelInfo model)
                e.Value = RamEstimator.FormatGiB(RamEstimator.EstimateRequiredRamBytes(model));
        };
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
