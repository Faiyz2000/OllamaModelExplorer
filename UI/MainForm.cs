using System.ComponentModel;
using System.Globalization;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;
using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

public sealed class MainForm : Form
{
    private readonly Database _db = new();
    private readonly OllamaScanner _scanner = new();

    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly ComboBox _size = new();
    private readonly CheckBox _installed = new();
    private readonly CheckBox _enriched = new();
    private readonly CheckBox _newOnOllama = new();
    private readonly DataGridView _grid = new();
    private readonly Label _summary = new();
    private readonly Button _scanButton = new();
    private readonly Button _selectFolderButton = new();

    private string _ollamaRoot = "";
    private List<ModelInfo> _allModels = new();

    private int _sortColumn = 1;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;

    public MainForm()
    {
        Text = "Ollama Model Explorer";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 850;
        MinimumSize = new Size(1100, 650);

        BuildUi();
        LoadModels();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.FromArgb(25, 29, 44) };
        var title = new Label
        {
            Text = "OLLAMA MODEL EXPLORER",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            Location = new Point(28, 14),
            AutoSize = true
        };
        _summary.Text = "No local models scanned.";
        _summary.ForeColor = Color.Silver;
        _summary.Location = new Point(29, 50);
        _summary.AutoSize = true;
        header.Controls.Add(title);
        header.Controls.Add(_summary);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 50,
            Padding = new Padding(18, 8, 18, 6),
            WrapContents = false
        };

        _selectFolderButton.Text = "Select Ollama Folder";
        _selectFolderButton.AutoSize = true;
        _selectFolderButton.Click += async (_, _) => await SelectFolderAsync();

        _scanButton.Text = "Scan Local Models";
        _scanButton.AutoSize = true;
        _scanButton.Click += async (_, _) => await ScanLocalAsync();

        var update = new Button { Text = "Update From Ollama.com", AutoSize = true };
        update.Click += (_, _) =>
            MessageBox.Show(this,
                "The online catalog updater remains isolated from the local scanner. " +
                "Approve an update only when you want to retrieve public Ollama metadata.",
                "Catalog update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

        var compare = new Button { Text = "Compare Selected", AutoSize = true };
        compare.Click += (_, _) => CompareSelected();

        toolbar.Controls.AddRange(new Control[] { _selectFolderButton, _scanButton, update, compare });

        var filter = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(18, 7, 18, 5),
            WrapContents = false
        };

        _search.Width = 260;
        _search.PlaceholderText = "Search model / publisher / description";
        _search.TextChanged += (_, _) => ApplyFilters();

        _category.Width = 150;
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Items.Add("All categories");
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += Category_SelectedIndexChanged;

        _size.Width = 130;
        _size.DropDownStyle = ComboBoxStyle.DropDownList;
        _size.Items.AddRange(new object[]
        {
            "All sizes", "0–10 GB", "11–20 GB", "21–50 GB", "51–100 GB", "100+ GB"
        });
        _size.SelectedIndex = 0;
        _size.SelectedIndexChanged += (_, _) => ApplyFilters();

        _installed.Text = "Installed";
        _installed.Checked = true;
        _installed.AutoSize = true;
        _installed.CheckedChanged += (_, _) => ApplyFilters();

        _enriched.Text = "Enriched";
        _enriched.AutoSize = true;
        _enriched.CheckedChanged += (_, _) => ApplyFilters();

        _newOnOllama.Text = "New on Ollama";
        _newOnOllama.AutoSize = true;
        _newOnOllama.CheckedChanged += (_, _) => ApplyFilters();

        filter.Controls.AddRange(new Control[]
        {
            _search, _category, _size, _installed, _enriched, _newOnOllama
        });

        ConfigureGrid();
        Controls.Add(_grid);
        Controls.Add(filter);
        Controls.Add(toolbar);
        Controls.Add(header);
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoGenerateColumns = false;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ScrollBars = ScrollBars.Vertical;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        _grid.RowTemplate.Height = 30;

        AddColumn("#", "RowNumber", 55, typeof(int));
        AddColumn("Name", "DisplayName", 300);
        AddColumn("Size", "SizeDisplay", 100);
        AddColumn("Publisher", "Publisher", 140);
        AddColumn("Parameters", "ParameterSize", 110);
        AddColumn("Family", "Family", 120);
        AddColumn("Quantization", "Quantization", 120);
        AddColumn("Categories", "CategoryText", 230);
        AddColumn("Modified", "ModifiedDisplay", 150);
        AddColumn("Metadata", "MetadataDisplay", 100);

        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                if (_grid.Rows[e.RowIndex].DataBoundItem is ModelRow row)
                    ShowDetails(row.Model);
            }
        };
    }

    private void AddColumn(string header, string property, int width, Type? type = null)
    {
        var c = new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic,
            ValueType = type
        };
        _grid.Columns.Add(c);
    }

    private void LoadModels()
    {
        _allModels = _db.GetAll();
        foreach (var m in _allModels)
            CategoryService.ApplyHeuristics(m);

        RefreshCategoryList();
        ApplyFilters();
    }

    private async Task SelectFolderAsync()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the root of your Ollama model storage",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _ollamaRoot = dialog.SelectedPath;
        await ScanLocalAsync();
    }

    private async Task ScanLocalAsync()
    {
        if (string.IsNullOrWhiteSpace(_ollamaRoot))
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the root containing blobs and manifests",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            _ollamaRoot = dialog.SelectedPath;
        }

        try
        {
            UseWaitCursor = true;
            _scanButton.Enabled = false;
            _selectFolderButton.Enabled = false;

            var progress = new Progress<string>(message =>
            {
                _summary.Text = message;
            });

            // IMPORTANT: Ollama /api/tags is the authoritative inventory.
            // The selected folder is validated, but we do not limit the
            // inventory to whatever manifest files happen to be discovered.
            var models = await _scanner.ScanAsync(_ollamaRoot, progress);

            foreach (var m in models)
                CategoryService.ApplyHeuristics(m);

            var upsertResult = _db.UpsertLocalModels(models);
            _db.MarkInstalledModels(models);

            _allModels = _db.GetAll();

            foreach (var m in _allModels)
                CategoryService.ApplyHeuristics(m);

            RefreshCategoryList();
            ApplyFilters();

            var installedCount = _allModels.Count(x => x.Installed);

            var summary =
                $"Ollama API reports: {models.Count} models{Environment.NewLine}" +
                $"Saved/updated: {upsertResult.Succeeded}{Environment.NewLine}" +
                $"Installed models in grid: {installedCount}";

            if (upsertResult.Failed.Count > 0)
            {
                summary +=
                    $"{Environment.NewLine}{Environment.NewLine}" +
                    $"Failed records: {upsertResult.Failed.Count}{Environment.NewLine}" +
                    string.Join(Environment.NewLine,
                        upsertResult.Failed.Take(10)
                            .Select(f => $"  {f.ManifestPath} -> {f.Error}"));
            }

            MessageBox.Show(
                this,
                summary,
                upsertResult.Failed.Count == 0
                    ? "Ollama scan complete"
                    : "Ollama scan completed with errors",
                MessageBoxButtons.OK,
                upsertResult.Failed.Count == 0
                    ? MessageBoxIcon.Information
                    : MessageBoxIcon.Warning);
        }
        catch (HttpRequestException)
        {
            MessageBox.Show(
                this,
                "The Ollama local API could not be reached at:" +
                Environment.NewLine +
                "http://localhost:11434" +
                Environment.NewLine + Environment.NewLine +
                "Make sure Ollama is running, then scan again.",
                "Ollama API unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Scan error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _scanButton.Enabled = true;
            _selectFolderButton.Enabled = true;
        }
    }

    private void RefreshCategoryList()
    {
        var current = _category.SelectedItem?.ToString();
        var categories = _allModels
            .SelectMany(x => x.CategoryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        _category.SelectedIndexChanged -= Category_SelectedIndexChanged;
        _category.Items.Clear();
        _category.Items.Add("All categories");
        foreach (var c in categories)
            _category.Items.Add(c);

        int index = string.IsNullOrWhiteSpace(current)
            ? 0
            : Math.Max(0, _category.Items.IndexOf(current));
        _category.SelectedIndex = index;
        _category.SelectedIndexChanged += Category_SelectedIndexChanged;
    }

    private void Category_SelectedIndexChanged(object? sender, EventArgs e)
    {
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        IEnumerable<ModelInfo> q = _allModels;

        if (_installed.Checked)
            q = q.Where(x => x.Installed);

        if (_enriched.Checked)
            q = q.Where(x => x.MetadataUpdatedUtc.HasValue);

        if (_newOnOllama.Checked)
            q = q.Where(x => x.NewOnOllama);

        var search = _search.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            q = q.Where(x =>
                x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (_category.SelectedIndex > 0)
        {
            var cat = _category.SelectedItem?.ToString() ?? "";
            q = q.Where(x => x.CategoryText
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(cat, StringComparer.OrdinalIgnoreCase));
        }

        q = ApplySizeFilter(q);

        var rows = q.Select((m, i) => new ModelRow(m, i + 1)).ToList();
        SortRows(rows);
        ReNumber(rows);

        _grid.DataSource = new BindingList<ModelRow>(rows);
        _summary.Text =
            $"{rows.Count} shown • {_allModels.Count(x => x.Installed)} installed • " +
            $"{FormatBytes(_allModels.Where(x => x.Installed).Sum(x => x.SizeBytes))} local storage • " +
            $"{_allModels.Count(x => x.MetadataUpdatedUtc.HasValue)} enriched";
    }

    private IEnumerable<ModelInfo> ApplySizeFilter(IEnumerable<ModelInfo> source)
    {
        return _size.SelectedIndex switch
        {
            1 => source.Where(x => x.SizeBytes >= 0 && x.SizeBytes < 10L * 1024 * 1024 * 1024),
            2 => source.Where(x => x.SizeBytes >= 10L * 1024 * 1024 * 1024 && x.SizeBytes < 20L * 1024 * 1024 * 1024),
            3 => source.Where(x => x.SizeBytes >= 20L * 1024 * 1024 * 1024 && x.SizeBytes < 50L * 1024 * 1024 * 1024),
            4 => source.Where(x => x.SizeBytes >= 50L * 1024 * 1024 * 1024 && x.SizeBytes < 100L * 1024 * 1024 * 1024),
            5 => source.Where(x => x.SizeBytes >= 100L * 1024 * 1024 * 1024),
            _ => source
        };
    }

    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0)
            return;

        if (_sortColumn == e.ColumnIndex)
            _sortDirection = _sortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
        else
        {
            _sortColumn = e.ColumnIndex;
            _sortDirection = ListSortDirection.Ascending;
        }

        ApplyFilters();
    }

    private void SortRows(List<ModelRow> rows)
    {
        Comparison<ModelRow>? comparison = _sortColumn switch
        {
            0 => (a, b) => a.RowNumber.CompareTo(b.RowNumber),
            1 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.DisplayName, b.Model.DisplayName),
            2 => (a, b) => a.Model.SizeBytes.CompareTo(b.Model.SizeBytes),
            3 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Publisher, b.Model.Publisher),
            4 => (a, b) => CompareNatural(a.Model.ParameterSize, b.Model.ParameterSize),
            5 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Family, b.Model.Family),
            6 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.Quantization, b.Model.Quantization),
            7 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.CategoryText, b.Model.CategoryText),
            8 => (a, b) => a.Model.ModifiedUtc.CompareTo(b.Model.ModifiedUtc),
            9 => (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Model.MetadataUpdatedUtc?.ToString("O") ?? "", b.Model.MetadataUpdatedUtc?.ToString("O") ?? ""),
            _ => null
        };

        if (comparison == null)
            return;

        rows.Sort(_sortDirection == ListSortDirection.Ascending
            ? comparison
            : (a, b) => comparison(b, a));
    }

    private static int CompareNatural(string a, string b)
    {
        double pa = ParseParameter(a);
        double pb = ParseParameter(b);
        if (pa >= 0 && pb >= 0)
            return pa.CompareTo(pb);

        return StringComparer.OrdinalIgnoreCase.Compare(a, b);

        static double ParseParameter(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return -1;

            var t = s.Trim().ToUpperInvariant();
            var digits = new string(t.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
            if (!double.TryParse(digits.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return -1;

            if (t.Contains('B')) return n * 1_000_000_000;
            if (t.Contains('M')) return n * 1_000_000;
            return n;
        }
    }

    private static void ReNumber(List<ModelRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i].RowNumber = i + 1;
    }

    private void ShowDetails(ModelInfo m)
    {
        var text =
            $"Model: {m.DisplayName}\r\n" +
            $"Publisher: {m.Publisher}\r\n" +
            $"Size: {FormatBytes(m.SizeBytes)}\r\n" +
            $"Modified: {m.ModifiedUtc:G}\r\n" +
            $"Parameters: {m.ParameterSize}\r\n" +
            $"Family: {m.Family}\r\n" +
            $"Quantization: {m.Quantization}\r\n" +
            $"Format: {m.Format}\r\n" +
            $"Context: {m.Context}\r\n" +
            $"Categories: {m.CategoryText}\r\n" +
            $"Capabilities: {m.Capabilities.Replace("|", ", ")}\r\n" +
            $"Metadata updated: {m.MetadataUpdatedUtc?.ToString("G") ?? "No"}\r\n" +
            $"Installed: {m.Installed}\r\n" +
            $"Manifest: {m.ManifestPath}\r\n" +
            $"Digest: {m.Digest}\r\n" +
            $"Ollama URL: {m.OllamaUrl}\r\n\r\n" +
            m.Description;

        using var f = new Form
        {
            Text = m.DisplayName,
            Width = 850,
            Height = 600,
            StartPosition = FormStartPosition.CenterParent
        };

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            Text = text,
            Font = new Font("Consolas", 10)
        };

        f.Controls.Add(box);
        f.ShowDialog(this);
    }

    private void CompareSelected()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Select(x => x.DataBoundItem as ModelRow)
            .Where(x => x is not null)
            .Select(x => x!.Model)
            .DistinctBy(x => x.Id)
            .ToList();

        if (selected.Count < 2)
        {
            MessageBox.Show(this, "Select at least two model rows.", "Compare");
            return;
        }

        var lines = new List<string>
        {
            "FEATURE".PadRight(24) +
            string.Join(" | ", selected.Select(x => x.DisplayName))
        };

        lines.Add(new string('-', Math.Min(160, lines[0].Length)));

        var comparisonFields = new (string Name, Func<ModelInfo, string> GetValue)[]
        {
            ("Size", m => FormatBytes(m.SizeBytes)),
            ("Parameters", m => m.ParameterSize),
            ("Family", m => m.Family),
            ("Quantization", m => m.Quantization),
            ("Context", m => m.Context),
            ("Categories", m => m.CategoryText),
            ("Capabilities", m => m.Capabilities.Replace("|", ", "))
        };

        foreach (var pair in comparisonFields)
        {
            lines.Add(
                pair.Name.PadRight(24) +
                string.Join(" | ", selected.Select(m => pair.GetValue(m))));
        }

        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Model comparison");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double value = bytes;
        string[] units = { "KB", "MB", "GB", "TB" };
        int i = -1;
        do
        {
            value /= 1024;
            i++;
        } while (value >= 1024 && i < units.Length - 1);

        return $"{value:0.##} {units[i]}";
    }

    private sealed class ModelRow
    {
        public ModelInfo Model { get; }
        public int RowNumber { get; set; }

        public ModelRow(ModelInfo model, int rowNumber)
        {
            Model = model;
            RowNumber = rowNumber;
        }

        public string SizeDisplay => FormatBytes(Model.SizeBytes);
        public string DisplayName => Model.DisplayName;
        public string Publisher => Model.Publisher;
        public string ParameterSize => Model.ParameterSize;
        public string Family => Model.Family;
        public string Quantization => Model.Quantization;
        public string CategoryText => Model.CategoryText;
        public string ModifiedDisplay => Model.ModifiedUtc.ToString("yyyy-MM-dd HH:mm");
        public string MetadataDisplay => Model.MetadataUpdatedUtc.HasValue ? "Enriched" : "Local";
    }
}
