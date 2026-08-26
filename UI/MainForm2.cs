using System.ComponentModel;
using System.Globalization;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;
using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

/// <summary>
/// Live inventory UI. The DataGridView is populated directly from /api/tags
/// after every scan. SQLite is used as a cache, but database failures cannot
/// hide models reported by Ollama.
/// </summary>
public sealed class MainForm2 : Form
{
    private readonly Database _db = new();
    private readonly OllamaScanner _scanner = new();
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _folder = new();
    private readonly Button _scan = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly ComboBox _size = new();
    private readonly CheckBox _installed = new();

    private string _root = "";
    private List<ModelInfo> _models = new();
    private int _sortColumn = 1;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public MainForm2()
    {
        Text = "Ollama Model Explorer";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 850;
        MinimumSize = new Size(1100, 650);
        BuildUi();
        LoadCachedModels();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(25, 29, 44) };
        var title = new Label { Text = "OLLAMA MODEL EXPLORER", ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(20, 10), AutoSize = true };
        _status.Text = "No live Ollama scan performed.";
        _status.ForeColor = Color.Silver;
        _status.Location = new Point(22, 45);
        _status.AutoSize = true;
        header.Controls.Add(title);
        header.Controls.Add(_status);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(15, 7, 15, 5), WrapContents = false };
        _folder.Text = "Select Ollama Folder";
        _folder.AutoSize = true;
        _folder.Click += async (_, _) => await SelectFolderAsync();
        _scan.Text = "Refresh Ollama Models";
        _scan.AutoSize = true;
        _scan.Click += async (_, _) => await ScanAsync();
        toolbar.Controls.Add(_folder);
        toolbar.Controls.Add(_scan);

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(15, 6, 15, 5), WrapContents = false };
        _search.Width = 300;
        _search.PlaceholderText = "Search model / publisher / family";
        _search.TextChanged += (_, _) => ApplyFilters();
        _category.Width = 150;
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Items.Add("All categories");
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += (_, _) => ApplyFilters();
        _size.Width = 130;
        _size.DropDownStyle = ComboBoxStyle.DropDownList;
        _size.Items.AddRange(new object[] { "All sizes", "0–10 GB", "11–20 GB", "21–50 GB", "51–100 GB", "100+ GB" });
        _size.SelectedIndex = 0;
        _size.SelectedIndexChanged += (_, _) => ApplyFilters();
        _installed.Text = "Installed only";
        _installed.Checked = true;
        _installed.AutoSize = true;
        _installed.CheckedChanged += (_, _) => ApplyFilters();
        filters.Controls.AddRange(new Control[] { _search, _category, _size, _installed });

        ConfigureGrid();
        Controls.Add(_grid);
        Controls.Add(filters);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.AllowUserToResizeRows = false;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.RowTemplate.Height = 30;

        AddColumn("#", "Number", 55);
        AddColumn("Name", "DisplayName", 330);
        AddColumn("Size", "SizeText", 100);
        AddColumn("Publisher", "Publisher", 150);
        AddColumn("Parameters", "ParameterSize", 110);
        AddColumn("Family", "Family", 130);
        AddColumn("Quantization", "Quantization", 120);
        AddColumn("Categories", "CategoryText", 240);
        AddColumn("Modified", "ModifiedText", 150);

        _grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (_sortColumn == e.ColumnIndex)
                _sortDirection = _sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
            else
            {
                _sortColumn = e.ColumnIndex;
                _sortDirection = ListSortDirection.Ascending;
            }
            ApplyFilters();
        };

        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is ModelRow row)
                ShowDetails(row.Model);
        };
    }

    private void AddColumn(string header, string property, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic
        });
    }

    private void LoadCachedModels()
    {
        try
        {
            _models = _db.GetAll();
            foreach (var m in _models) CategoryService.ApplyHeuristics(m);
            RefreshCategories();
            ApplyFilters();
        }
        catch (Exception ex)
        {
            _status.Text = "Database cache unavailable; live scanning is still available: " + ex.Message;
        }
    }

    private async Task SelectFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Ollama model root containing blobs and manifests",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _root = dialog.SelectedPath;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(_root))
        {
            using var dialog = new FolderBrowserDialog { Description = "Select the Ollama model root", ShowNewFolderButton = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _root = dialog.SelectedPath;
        }

        try
        {
            _folder.Enabled = false;
            _scan.Enabled = false;
            UseWaitCursor = true;

            var progress = new Progress<string>(s => _status.Text = s);
            var liveModels = await _scanner.ScanAsync(_root, progress);

            // The live API result is the source shown in the grid. Do not
            // replace it with a possibly incomplete/old SQLite result.
            foreach (var model in liveModels) CategoryService.ApplyHeuristics(model);
            _models = liveModels;
            RefreshCategories();
            ApplyFilters();

            UpsertResult cacheResult;
            try
            {
                cacheResult = _db.UpsertLocalModels(liveModels);
                _db.MarkInstalledModels(liveModels);
            }
            catch (Exception ex)
            {
                cacheResult = new UpsertResult(0, new List<(string ManifestPath, string Error)> { ("database", ex.Message) });
            }

            // Re-apply the live list after SQLite operations. This is deliberate:
            // database/cache state must never reduce the live API inventory.
            _models = liveModels;
            ApplyFilters();

            var failed = cacheResult.Failed.Count;
            _status.Text = failed == 0
                ? $"LIVE: {liveModels.Count} models from Ollama • SQLite cache updated: {cacheResult.Succeeded}"
                : $"LIVE: {liveModels.Count} models from Ollama • SQLite cache failures: {failed} (models remain visible)";

            MessageBox.Show(this,
                $"Ollama API reported: {liveModels.Count} models\r\n" +
                $"Models displayed in grid: {_models.Count}\r\n" +
                $"SQLite records saved/updated: {cacheResult.Succeeded}\r\n" +
                (failed == 0 ? "\r\nNo database errors." : "\r\nDatabase errors occurred, but they did NOT remove models from the grid."),
                "Ollama scan complete",
                failed == 0 ? MessageBoxButtons.OK : MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(this, "Cannot reach Ollama at http://localhost:11434.\r\n\r\n" + ex.Message, "Ollama API unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scan error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _folder.Enabled = true;
            _scan.Enabled = true;
        }
    }

    private void RefreshCategories()
    {
        _category.SelectedIndexChanged -= CategoryChanged;
        var current = _category.SelectedItem?.ToString();
        _category.Items.Clear();
        _category.Items.Add("All categories");
        foreach (var category in _models.SelectMany(m => m.CategoryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            _category.Items.Add(category);
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += CategoryChanged;
    }

    private void CategoryChanged(object? sender, EventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        IEnumerable<ModelInfo> query = _models;
        if (_installed.Checked) query = query.Where(x => x.Installed);
        var text = _search.Text.Trim();
        if (text.Length > 0)
            query = query.Where(x => x.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) || x.Publisher.Contains(text, StringComparison.OrdinalIgnoreCase) || x.Family.Contains(text, StringComparison.OrdinalIgnoreCase));
        if (_category.SelectedIndex > 0)
        {
            var category = _category.SelectedItem?.ToString() ?? "";
            query = query.Where(x => x.CategoryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(category, StringComparer.OrdinalIgnoreCase));
        }
        query = _size.SelectedIndex switch
        {
            1 => query.Where(x => x.SizeBytes < 10L * 1024 * 1024 * 1024),
            2 => query.Where(x => x.SizeBytes >= 10L * 1024 * 1024 * 1024 && x.SizeBytes < 20L * 1024 * 1024 * 1024),
            3 => query.Where(x => x.SizeBytes >= 20L * 1024 * 1024 * 1024 && x.SizeBytes < 50L * 1024 * 1024 * 1024),
            4 => query.Where(x => x.SizeBytes >= 50L * 1024 * 1024 * 1024 && x.SizeBytes < 100L * 1024 * 1024 * 1024),
            5 => query.Where(x => x.SizeBytes >= 100L * 1024 * 1024 * 1024),
            _ => query
        };

        var rows = query.Select((m, i) => new ModelRow(m, i + 1)).ToList();
        Comparison<ModelRow> comparison = _sortColumn switch
        {
            0 => (a, b) => a.Number.CompareTo(b.Number),
            2 => (a, b) => a.Model.SizeBytes.CompareTo(b.Model.SizeBytes),
            3 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Publisher, b.Model.Publisher),
            4 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.ParameterSize, b.Model.ParameterSize),
            5 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Family, b.Model.Family),
            6 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Quantization, b.Model.Quantization),
            7 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.CategoryText, b.Model.CategoryText),
            8 => (a, b) => a.Model.ModifiedUtc.CompareTo(b.Model.ModifiedUtc),
            _ => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.DisplayName, b.Model.DisplayName)
        };
        rows.Sort(_sortDirection == ListSortDirection.Ascending ? comparison : (a, b) => comparison(b, a));
        for (int i = 0; i < rows.Count; i++) rows[i].Number = i + 1;
        _grid.DataSource = new BindingList<ModelRow>(rows);
        _status.Text = $"{rows.Count} shown / {_models.Count} models from current Ollama scan";
    }

    private void ShowDetails(ModelInfo m)
    {
        var text = $"Model: {m.DisplayName}\r\nPublisher: {m.Publisher}\r\nSize: {FormatBytes(m.SizeBytes)}\r\nModified: {m.ModifiedUtc:G}\r\nParameters: {m.ParameterSize}\r\nFamily: {m.Family}\r\nQuantization: {m.Quantization}\r\nFormat: {m.Format}\r\nContext: {m.Context}\r\nCategories: {m.CategoryText}\r\nCapabilities: {m.Capabilities.Replace("|", ", ")}\r\nDigest: {m.Digest}\r\nOllama URL: {m.OllamaUrl}";
        MessageBox.Show(this, text, m.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int i = -1;
        do { value /= 1024; i++; } while (value >= 1024 && i < units.Length - 1);
        return $"{value:0.##} {units[i]}";
    }

    private sealed class ModelRow
    {
        public ModelInfo Model { get; }
        public int Number { get; set; }
        public string DisplayName => Model.DisplayName;
        public string SizeText => FormatBytes(Model.SizeBytes);
        public string Publisher => Model.Publisher;
        public string ParameterSize => Model.ParameterSize;
        public string Family => Model.Family;
        public string Quantization => Model.Quantization;
        public string CategoryText => Model.CategoryText;
        public string ModifiedText => Model.ModifiedUtc == DateTime.MinValue ? "Unknown" : Model.ModifiedUtc.ToString("yyyy-MM-dd HH:mm");
        public ModelRow(ModelInfo model, int number) { Model = model; Number = number; }
    }
}
