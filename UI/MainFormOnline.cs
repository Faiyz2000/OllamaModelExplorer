using System.ComponentModel;
using System.Globalization;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;
using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

public sealed class MainFormOnline : Form
{
    private readonly Database _db = new();
    private readonly OllamaScanner _scanner = new();
    private readonly OllamaOnlineCatalogService _online = new();

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
    private readonly Button _updateButton = new();
    private readonly Button _checkNewButton = new();
    private readonly System.Windows.Forms.Timer _ramRefreshTimer = new() { Interval = 5000 };

    private string _ollamaRoot = "";
    private List<ModelInfo> _allModels = new();
    private List<OllamaOnlineCatalogService.OnlineModel> _newOnline = new();
    private int _sortColumn = 1;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _startupScanStarted;

    public MainFormOnline()
    {
        Text = "Ollama Model Explorer";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 850;
        MinimumSize = new Size(1100, 650);
        AppLogger.Info("Application started.");
        BuildUi();
        LoadCachedMetadataOnly();
        Shown += async (_, _) => await StartupScanAsync();
        _ramRefreshTimer.Tick += (_, _) => RefreshRamDisplay();
        _ramRefreshTimer.Start();
        FormClosed += (_, _) => _ramRefreshTimer.Stop();
    }

    private void BuildUi()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 86, BackColor = Color.FromArgb(25, 29, 44) };
        var title = new Label { Text = "OLLAMA MODEL EXPLORER", ForeColor = Color.White, Font = new Font("Segoe UI", 18, FontStyle.Bold), Location = new Point(28, 14), AutoSize = true };
        _summary.Text = "Waiting for Ollama local scan...";
        _summary.ForeColor = Color.Silver;
        _summary.Location = new Point(29, 50);
        _summary.AutoSize = true;
        header.Controls.Add(title);
        header.Controls.Add(_summary);

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(18, 8, 18, 6), WrapContents = false, AutoScroll = true };
        _selectFolderButton.Text = "Select Ollama Folder";
        _selectFolderButton.AutoSize = true;
        _selectFolderButton.Click += async (_, _) => await SelectFolderAsync();
        _scanButton.Text = "Scan Local Models";
        _scanButton.AutoSize = true;
        _scanButton.Click += async (_, _) => await ScanLocalAsync();
        _updateButton.Text = "Update From Ollama.com";
        _updateButton.AutoSize = true;
        _updateButton.Click += async (_, _) => await UpdateOnlineAsync();
        _checkNewButton.Text = "Check for New";
        _checkNewButton.AutoSize = true;
        _checkNewButton.Click += async (_, _) => await CheckForNewAsync();
        var compare = new Button { Text = "Compare Selected", AutoSize = true };
        compare.Click += (_, _) => CompareSelected();
        var log = new Button { Text = "View Log", AutoSize = true };
        log.Click += (_, _) => { AppLogger.Action("Log viewer opened."); using var f = new LogForm(); f.ShowDialog(this); };
        toolbar.Controls.AddRange(new Control[] { _selectFolderButton, _scanButton, _updateButton, _checkNewButton, compare, log });

        var filter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(18, 7, 18, 5), WrapContents = false, AutoScroll = true };
        _search.Width = 260;
        _search.PlaceholderText = "Search model / publisher / description";
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
        filter.Controls.AddRange(new Control[] { _search, _category, _size, _installed, _enriched, _newOnOllama });

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
        _grid.RowHeadersVisible = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
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
        // RAM is deliberately the final/right-most column.
        AddColumn("RAM to Run", "RamRequirement", 240);
        _grid.ColumnHeaderMouseClick += Grid_ColumnHeaderMouseClick;
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Rows[e.RowIndex].DataBoundItem is ModelRow row)
                ShowDetails(row.Model);
        };
    }

    private void AddColumn(string header, string property, int width, Type? type = null)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            DataPropertyName = property,
            Width = width,
            SortMode = DataGridViewColumnSortMode.Programmatic,
            ValueType = type
        });
    }

    private void LoadCachedMetadataOnly()
    {
        // The database/cache is used only for previously downloaded online metadata.
        // It is NOT used to decide which models are currently installed.
        _allModels = _db.GetAll();
        ApplyCachedOnlineMetadata();
        foreach (var m in _allModels) CategoryService.ApplyHeuristics(m);
        RefreshCategoryList();
        ApplyFilters();
        AppLogger.Info($"Loaded {_allModels.Count} cached records while waiting for fresh Ollama scan.");
    }

    private async Task StartupScanAsync()
    {
        if (_startupScanStarted) return;
        _startupScanStarted = true;
        _ollamaRoot = ResolveOllamaRoot();
        if (string.IsNullOrWhiteSpace(_ollamaRoot))
        {
            _summary.Text = "Select the Ollama models folder to perform the initial fresh scan.";
            return;
        }
        AppLogger.Info($"Automatic startup scan using Ollama root: {_ollamaRoot}");
        await ScanLocalAsync(false);
    }

    private static string ResolveOllamaRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("OLLAMA_MODELS"),
            Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.Machine),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ollama", "models")
        };
        foreach (var candidate in candidates.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            try
            {
                if (Directory.Exists(candidate) &&
                    Directory.Exists(Path.Combine(candidate!, "blobs")) &&
                    Directory.Exists(Path.Combine(candidate!, "manifests")))
                    return candidate!;
            }
            catch { }
        }
        return "";
    }

    private void ApplyCachedOnlineMetadata()
    {
        var cache = _online.LoadCache();
        var map = cache.ToDictionary(x => $"{x.Publisher}/{x.Name}", StringComparer.OrdinalIgnoreCase);
        foreach (var m in _allModels)
        {
            if (!map.TryGetValue($"{m.Publisher}/{m.Name}", out var online)) continue;
            if (!string.IsNullOrWhiteSpace(online.Description)) m.Description = online.Description;
            if (!string.IsNullOrWhiteSpace(online.Capabilities)) m.Capabilities = online.Capabilities;
            if (!string.IsNullOrWhiteSpace(online.OllamaUrl)) m.OllamaUrl = online.OllamaUrl;
            if (!m.MetadataUpdatedUtc.HasValue && online.SeenUtc != default)
                m.MetadataUpdatedUtc = online.SeenUtc.ToLocalTime();
        }
    }

    private void MergePreviouslyCachedMetadata(List<ModelInfo> freshModels)
    {
        var stored = _db.GetAll().ToDictionary(x => $"{x.Publisher}/{x.Name}:{x.Tag}", StringComparer.OrdinalIgnoreCase);
        var onlineCache = _online.LoadCache().ToDictionary(x => $"{x.Publisher}/{x.Name}", StringComparer.OrdinalIgnoreCase);
        foreach (var m in freshModels)
        {
            var key = $"{m.Publisher}/{m.Name}:{m.Tag}";
            if (stored.TryGetValue(key, out var old))
            {
                if (string.IsNullOrWhiteSpace(m.Description)) m.Description = old.Description;
                if (string.IsNullOrWhiteSpace(m.Capabilities)) m.Capabilities = old.Capabilities;
                if (string.IsNullOrWhiteSpace(m.OllamaUrl)) m.OllamaUrl = old.OllamaUrl;
                if (!m.MetadataUpdatedUtc.HasValue) m.MetadataUpdatedUtc = old.MetadataUpdatedUtc;
            }
            if (onlineCache.TryGetValue($"{m.Publisher}/{m.Name}", out var online))
            {
                if (string.IsNullOrWhiteSpace(m.Description)) m.Description = online.Description;
                if (string.IsNullOrWhiteSpace(m.Capabilities)) m.Capabilities = online.Capabilities;
                if (string.IsNullOrWhiteSpace(m.OllamaUrl)) m.OllamaUrl = online.OllamaUrl;
                if (!m.MetadataUpdatedUtc.HasValue && online.SeenUtc != default) m.MetadataUpdatedUtc = online.SeenUtc.ToLocalTime();
            }
        }
    }

    private async Task SelectFolderAsync()
    {
        AppLogger.Action("Select Ollama Folder clicked.");
        using var dialog = new FolderBrowserDialog { Description = "Select the root of your Ollama model storage", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _ollamaRoot = dialog.SelectedPath;
        await ScanLocalAsync(true);
    }

    private async Task ScanLocalAsync(bool showResult = true)
    {
        if (string.IsNullOrWhiteSpace(_ollamaRoot))
        {
            using var dialog = new FolderBrowserDialog { Description = "Select the root containing blobs and manifests", ShowNewFolderButton = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            _ollamaRoot = dialog.SelectedPath;
        }

        AppLogger.Action($"Starting fresh local model scan: {_ollamaRoot}");
        try
        {
            SetBusy(true);
            var progress = new Progress<string>(x => _summary.Text = x);
            var models = await _scanner.ScanAsync(_ollamaRoot, progress);
            foreach (var m in models) CategoryService.ApplyHeuristics(m);

            // Refresh the database with the current /api/tags set. The database is not
            // the source of truth for installed models; Ollama /api/tags is.
            MergePreviouslyCachedMetadata(models);
            var result = _db.UpsertLocalModels(models);
            _db.MarkInstalledModels(models);
            _allModels = models;
            foreach (var m in _allModels) CategoryService.ApplyHeuristics(m);
            RefreshCategoryList();
            ApplyFilters();

            var installedCount = _allModels.Count(x => x.Installed);
            var enrichedCount = _allModels.Count(x => x.MetadataUpdatedUtc.HasValue || !string.IsNullOrWhiteSpace(x.Description));
            AppLogger.Info($"Fresh scan completed. Ollama API={models.Count}, saved={result.Succeeded}, installed in grid={installedCount}, enriched={enrichedCount}, failed={result.Failed.Count}.");

            if (showResult)
            {
                var summary = $"Ollama /api/tags reports: {models.Count} models{Environment.NewLine}" +
                              $"Saved/updated: {result.Succeeded}{Environment.NewLine}" +
                              $"Models shown from fresh API result: {installedCount}{Environment.NewLine}" +
                              $"Metadata enriched: {enrichedCount}";
                if (result.Failed.Count > 0)
                    summary += Environment.NewLine + Environment.NewLine + "Database errors:" + Environment.NewLine +
                               string.Join(Environment.NewLine, result.Failed.Take(10).Select(f => $"• {f.ManifestPath}: {f.Error}"));
                MessageBox.Show(this, summary, result.Failed.Count == 0 ? "Ollama scan complete" : "Scan completed with errors", MessageBoxButtons.OK,
                    result.Failed.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Error("Ollama local API could not be reached.", ex);
            MessageBox.Show(this, "Ollama could not be reached at http://localhost:11434. Make sure Ollama is running." + Environment.NewLine + Environment.NewLine + ex.Message,
                "Ollama API unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Fresh local model scan failed.", ex);
            MessageBox.Show(this, ex.Message, "Scan error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task UpdateOnlineAsync()
    {
        var answer = MessageBox.Show(this,
            "Update from Ollama.com?\r\n\r\nThis will first refresh the local installed-model list from Ollama, then contact Ollama.com for metadata for every locally installed model and for the public newest-model catalog.\r\n\r\nNo model files, prompts, chats, or local file paths are uploaded.",
            "Online update approval", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        try
        {
            SetBusy(true);
            var progress = new Progress<string>(x => _summary.Text = x);
            var models = await _scanner.ScanAsync(_ollamaRoot, progress);
            foreach (var m in models) CategoryService.ApplyHeuristics(m);
            MergePreviouslyCachedMetadata(models);
            _db.UpsertLocalModels(models);
            _db.MarkInstalledModels(models);
            _allModels = models;

            var result = await _online.UpdateAsync(_allModels.Where(x => x.Installed).ToList(), progress);
            ApplyCachedOnlineMetadata();
            foreach (var m in _allModels) CategoryService.ApplyHeuristics(m);
            _db.UpsertLocalModels(_allModels);
            RefreshCategoryList();
            ApplyFilters();
            _newOnline = result.NewlyDiscovered.ToList();
            AppLogger.Info($"Ollama.com update completed. Catalog={result.CatalogModels}, installed pages updated={result.ExistingModelsUpdated}, new={result.NewlyDiscovered.Count}.");

            var message = $"Fresh local models: {_allModels.Count}{Environment.NewLine}" +
                          $"Ollama.com catalog entries: {result.CatalogModels}{Environment.NewLine}" +
                          $"Locally installed model pages updated: {result.ExistingModelsUpdated}{Environment.NewLine}" +
                          $"New Ollama.com models discovered: {result.NewlyDiscovered.Count}";
            if (result.NewlyDiscovered.Count > 0)
                message += Environment.NewLine + Environment.NewLine + "New models:" + Environment.NewLine +
                           string.Join(Environment.NewLine, result.NewlyDiscovered.Take(50).Select(x => "• " + x.Name));
            MessageBox.Show(this, message, "Ollama.com update complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Ollama.com update failed.", ex);
            MessageBox.Show(this, "Ollama.com update failed:" + Environment.NewLine + ex.Message, "Online update error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private async Task CheckForNewAsync()
    {
        var answer = MessageBox.Show(this,
            "Check Ollama.com for newly published models? This is an online operation and will not upload your local model list or other local data.",
            "Check for new models", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;
        try
        {
            SetBusy(true);
            var progress = new Progress<string>(x => _summary.Text = x);
            var result = await _online.CheckForNewAsync(progress);
            _newOnline = result.NewModels.ToList();
            AppLogger.Info($"New-model check completed. Catalog={result.CatalogModels}, new={result.NewModels.Count}.");
            var message = result.NewModels.Count == 0
                ? $"No new models were detected in the current Ollama.com newest-model catalog.\r\n\r\nCatalog entries checked: {result.CatalogModels}"
                : $"{result.NewModels.Count} new model(s) detected:\r\n\r\n" + string.Join("\r\n", result.NewModels.Take(50).Select(x => "• " + x.Name));
            MessageBox.Show(this, message, "Ollama.com new models", MessageBoxButtons.OK, MessageBoxIcon.Information);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            AppLogger.Error("New-model check failed.", ex);
            MessageBox.Show(this, "The check could not be completed:" + Environment.NewLine + ex.Message, "Online check error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _selectFolderButton.Enabled = !busy;
        _scanButton.Enabled = !busy;
        _updateButton.Enabled = !busy;
        _checkNewButton.Enabled = !busy;
    }

    private void RefreshCategoryList()
    {
        var current = _category.SelectedItem?.ToString();
        var categories = _allModels.SelectMany(x => x.CategoryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        _category.SelectedIndexChanged -= CategoryChanged;
        _category.Items.Clear();
        _category.Items.Add("All categories");
        foreach (var c in categories) _category.Items.Add(c);
        _category.SelectedIndex = string.IsNullOrWhiteSpace(current) ? 0 : Math.Max(0, _category.Items.IndexOf(current));
        _category.SelectedIndexChanged += CategoryChanged;
    }

    private void CategoryChanged(object? sender, EventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        IEnumerable<ModelInfo> q = _allModels;
        if (_installed.Checked) q = q.Where(x => x.Installed);
        if (_enriched.Checked) q = q.Where(x => x.MetadataUpdatedUtc.HasValue || !string.IsNullOrWhiteSpace(x.Description));
        if (_newOnOllama.Checked)
        {
            var keys = _newOnline.Select(x => $"{x.Publisher}/{x.Name}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            q = q.Where(x => keys.Contains($"{x.Publisher}/{x.Name}"));
        }
        var search = _search.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(x => x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase) || x.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (_category.SelectedIndex > 0)
        {
            var cat = _category.SelectedItem?.ToString() ?? "";
            q = q.Where(x => x.CategoryText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(cat, StringComparer.OrdinalIgnoreCase));
        }
        q = ApplySizeFilter(q);
        var rows = q.Select((m, i) => new ModelRow(m, i + 1)).ToList();
        SortRows(rows);
        ReNumber(rows);
        _grid.DataSource = new BindingList<ModelRow>(rows);
        UpdateSummary(rows.Count);
    }

    private void UpdateSummary(int shown)
    {
        var available = RamEstimator.GetAvailableRamBytes();
        var availableText = available > 0 ? RamEstimator.FormatGiB(available) : "Unknown";
        _summary.Text = $"{shown} shown • {_allModels.Count(x => x.Installed)} installed • {FormatBytes(_allModels.Where(x => x.Installed).Sum(x => x.SizeBytes))} local storage • {_allModels.Count(x => x.MetadataUpdatedUtc.HasValue || !string.IsNullOrWhiteSpace(x.Description))} enriched • {availableText} RAM available";
    }

    private void RefreshRamDisplay()
    {
        if (_grid.IsDisposed) return;
        if (RamEstimator.GetAvailableRamBytes() > 0) _grid.InvalidateColumn(_grid.Columns["RAM to Run"]?.Index ?? 10);
        UpdateSummary(_grid.Rows.Count);
    }

    private IEnumerable<ModelInfo> ApplySizeFilter(IEnumerable<ModelInfo> source) => _size.SelectedIndex switch
    {
        1 => source.Where(x => x.SizeBytes >= 0 && x.SizeBytes < 10L * 1024 * 1024 * 1024),
        2 => source.Where(x => x.SizeBytes >= 10L * 1024 * 1024 * 1024 && x.SizeBytes < 20L * 1024 * 1024 * 1024),
        3 => source.Where(x => x.SizeBytes >= 20L * 1024 * 1024 * 1024 && x.SizeBytes < 50L * 1024 * 1024 * 1024),
        4 => source.Where(x => x.SizeBytes >= 50L * 1024 * 1024 * 1024 && x.SizeBytes < 100L * 1024 * 1024 * 1024),
        5 => source.Where(x => x.SizeBytes >= 100L * 1024 * 1024 * 1024),
        _ => source
    };

    private void Grid_ColumnHeaderMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex < 0) return;
        if (_sortColumn == e.ColumnIndex) _sortDirection = _sortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else { _sortColumn = e.ColumnIndex; _sortDirection = ListSortDirection.Ascending; }
        ApplyFilters();
    }

    private void SortRows(List<ModelRow> rows)
    {
        Comparison<ModelRow>? c = _sortColumn switch
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
            10 => (a, b) => RamEstimator.EstimateRequiredRamBytes(a.Model).CompareTo(RamEstimator.EstimateRequiredRamBytes(b.Model)),
            _ => null
        };
        if (c is null) return;
        rows.Sort(_sortDirection == ListSortDirection.Ascending ? c : (a, b) => c(b, a));
    }

    private static int CompareNatural(string a, string b)
    {
        double pa = Parse(a), pb = Parse(b);
        if (pa >= 0 && pb >= 0) return pa.CompareTo(pb);
        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        static double Parse(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            var t = s.Trim().ToUpperInvariant();
            var digits = new string(t.TakeWhile(ch => char.IsDigit(ch) || ch == '.' || ch == ',').ToArray());
            if (!double.TryParse(digits.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return -1;
            if (t.Contains('B')) return n * 1_000_000_000;
            if (t.Contains('M')) return n * 1_000_000;
            return n;
        }
    }

    private static void ReNumber(List<ModelRow> rows) { for (int i = 0; i < rows.Count; i++) rows[i].RowNumber = i + 1; }

    private void ShowDetails(ModelInfo m)
    {
        AppLogger.Action($"Opened model details: {m.DisplayName}");
        var ram = RamEstimator.Assess(m);
        var available = RamEstimator.GetAvailableRamBytes();
        var text = $"MODEL INFORMATION\r\n==============================\r\n\r\n" +
                   $"Model:              {m.DisplayName}\r\n" +
                   $"Publisher:          {m.Publisher}\r\n" +
                   $"Tag:                {m.Tag}\r\n" +
                   $"Size:               {FormatBytes(m.SizeBytes)}\r\n" +
                   $"Modified:           {m.ModifiedUtc:G}\r\n" +
                   $"Parameters:         {m.ParameterSize}\r\n" +
                   $"Family:             {m.Family}\r\n" +
                   $"Quantization:       {m.Quantization}\r\n" +
                   $"Format:             {m.Format}\r\n" +
                   $"Context:            {m.Context}\r\n" +
                   $"RAM required:       {ram.Display}\r\n" +
                   $"RAM available now:  {(available > 0 ? RamEstimator.FormatGiB(available) : "Unknown")}\r\n" +
                   $"Categories:         {m.CategoryText}\r\n" +
                   $"Capabilities:       {m.Capabilities.Replace("|", ", ")}\r\n" +
                   $"Installed:          {m.Installed}\r\n" +
                   $"Metadata updated:   {m.MetadataUpdatedUtc?.ToString("G") ?? "No"}\r\n" +
                   $"Ollama URL:         {m.OllamaUrl}\r\n" +
                   $"Digest:             {m.Digest}\r\n" +
                   $"Manifest/API path:  {m.ManifestPath}\r\n\r\n" +
                   $"DESCRIPTION\r\n==============================\r\n{m.Description}";

        using var f = new Form
        {
            Text = $"Model Details - {m.DisplayName}",
            Width = 1000,
            Height = 720,
            MinimumSize = new Size(800, 550),
            StartPosition = FormStartPosition.CenterParent
        };
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Text = text,
            Font = new Font("Consolas", 14, FontStyle.Regular),
            BackColor = SystemColors.Window
        };
        f.Controls.Add(box);
        f.ShowDialog(this);
    }

    private void CompareSelected()
    {
        AppLogger.Action("Compare Selected clicked.");
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>().Select(x => x.DataBoundItem as ModelRow).Where(x => x is not null).Select(x => x!.Model).DistinctBy(x => x.Id).ToList();
        if (selected.Count < 2) { MessageBox.Show(this, "Select at least two model rows.", "Compare"); return; }
        var lines = new List<string> { "FEATURE".PadRight(24) + string.Join(" | ", selected.Select(x => x.DisplayName)), new string('-', 140) };
        foreach (var pair in new (string, Func<ModelInfo, string>)[]
        {
            ("Size", m => FormatBytes(m.SizeBytes)),
            ("Parameters", m => m.ParameterSize),
            ("Family", m => m.Family),
            ("Quantization", m => m.Quantization),
            ("Estimated RAM", m => RamEstimator.Assess(m).Display),
            ("Context", m => m.Context),
            ("Categories", m => m.CategoryText),
            ("Capabilities", m => m.Capabilities.Replace("|", ", "))
        })
            lines.Add(pair.Item1.PadRight(24) + string.Join(" | ", selected.Select(pair.Item2)));
        MessageBox.Show(this, string.Join(Environment.NewLine, lines), "Model comparison");
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
        public int RowNumber { get; set; }
        public ModelRow(ModelInfo model, int rowNumber) { Model = model; RowNumber = rowNumber; }
        public string SizeDisplay => FormatBytes(Model.SizeBytes);
        public string DisplayName => Model.DisplayName;
        public string Publisher => Model.Publisher;
        public string ParameterSize => string.IsNullOrWhiteSpace(Model.ParameterSize) ? "Unknown" : Model.ParameterSize;
        public string Family => Model.Family;
        public string Quantization => Model.Quantization;
        public string CategoryText => Model.CategoryText;
        public string ModifiedDisplay => Model.ModifiedUtc == DateTime.MinValue ? "Unknown" : Model.ModifiedUtc.ToString("yyyy-MM-dd HH:mm");
        public string MetadataDisplay => Model.MetadataUpdatedUtc.HasValue || !string.IsNullOrWhiteSpace(Model.Description) ? "Enriched" : "Local";
        public string RamRequirement => RamEstimator.Assess(Model).Display;
    }
}
