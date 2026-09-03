using System.ComponentModel;
using System.Collections;
using System.Reflection;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Adds the per-model RAM requirement column to the existing main model grid.
/// The value is an estimate of RAM required to load the model, not current free system RAM.
/// Supports ascending/descending sorting and a sort glyph.
/// </summary>
public static class RamColumnFeature
{
    private const string ColumnName = "RamRequiredColumn";
    private static bool _ramSortActive;
    private static ListSortDirection _ramSortDirection = ListSortDirection.Ascending;
    private static int _lastHeaderColumn = -1;

    public static void Attach(Form form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));

        var grid = FindControl<DataGridView>(form);
        if (grid is null) return;

        var column = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Name == ColumnName);
        if (column is null)
        {
            column = new DataGridViewTextBoxColumn
            {
                Name = ColumnName,
                HeaderText = "RAM Required",
                Width = 125,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic
            };

            var insertAt = Math.Min(3, grid.Columns.Count);
            grid.Columns.Insert(insertAt, column);
        }
        else
        {
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
        }

        var ramColumn = column;
        grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ramColumn.Index) return;
            if (grid.Rows[e.RowIndex].DataBoundItem is null) return;

            var property = grid.Rows[e.RowIndex].DataBoundItem.GetType()
                .GetProperty("Model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(grid.Rows[e.RowIndex].DataBoundItem) is ModelInfo model)
                e.Value = RamEstimator.FormatGiB(RamEstimator.EstimateRequiredRamBytes(model));
        };

        // MouseDown runs before MainFormOnline's ColumnHeaderMouseClick handler.
        // This lets RAM sorting remain the active sort when filters rebuild the data source.
        grid.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            var hit = grid.HitTest(e.X, e.Y);
            if (hit.Type != DataGridViewHitTestType.ColumnHeader) return;

            _lastHeaderColumn = hit.ColumnIndex;
            if (hit.ColumnIndex == ramColumn.Index)
            {
                _ramSortDirection = _ramSortActive && _ramSortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
                _ramSortActive = true;
            }
            else
            {
                _ramSortActive = false;
                ClearRamSortGlyph(grid, ramColumn);
            }
        };

        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.ColumnIndex != ramColumn.Index) return;
            SortRamRows(grid, ramColumn);
        };

        grid.DataSourceChanged += (_, _) =>
        {
            if (_ramSortActive)
                SortRamRows(grid, ramColumn);
        };
    }

    private static void SortRamRows(DataGridView grid, DataGridViewColumn ramColumn)
    {
        if (grid.DataSource is not IList list || list.Count < 2)
        {
            SetRamSortGlyph(ramColumn);
            return;
        }

        var items = list.Cast<object>().ToList();
        items.Sort((a, b) =>
        {
            var ma = GetModel(a);
            var mb = GetModel(b);
            var result = RamEstimator.EstimateRequiredRamBytes(ma).CompareTo(
                RamEstimator.EstimateRequiredRamBytes(mb));
            if (result == 0)
                result = StringComparer.OrdinalIgnoreCase.Compare(ma.DisplayName, mb.DisplayName);
            return _ramSortDirection == ListSortDirection.Ascending ? result : -result;
        });

        for (var i = 0; i < items.Count; i++)
            list[i] = items[i];

        SetRamSortGlyph(ramColumn);
    }

    private static ModelInfo GetModel(object row)
    {
        var property = row.GetType().GetProperty("Model", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(row) as ModelInfo
            ?? throw new InvalidOperationException("The RAM Required column could not identify the model row.");
    }

    private static void SetRamSortGlyph(DataGridViewColumn ramColumn)
    {
        ramColumn.HeaderCell.SortGlyphDirection = _ramSortDirection == ListSortDirection.Ascending
            ? SortOrder.Ascending
            : SortOrder.Descending;
    }

    private static void ClearRamSortGlyph(DataGridView grid, DataGridViewColumn ramColumn)
    {
        ramColumn.HeaderCell.SortGlyphDirection = SortOrder.None;
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
