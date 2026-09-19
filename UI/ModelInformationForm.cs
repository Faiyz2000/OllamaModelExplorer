using System.Text;
using System.Text.RegularExpressions;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;
using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

public sealed class ModelInformationForm : Form
{
    private readonly ModelInformationDatabase _database;
    private readonly List<ModelInfo> _models;
    private readonly List<ModelInformation> _history;
    private readonly WebBrowser _browser = new();
    private readonly Label _status = new();
    private readonly Label _fontSizeLabel = new();
    private readonly ComboBox _modelSelector = new();
    private float _fontSize = 10f;

    public ModelInformationForm(ModelInformationDatabase database, IReadOnlyList<ModelInfo> models, IReadOnlyList<ModelInformation> history)
    {
        _database = database;
        _models = models.ToList();
        _history = history.ToList();
        Text = "Model Information";
        Width = 1100;
        Height = 750;
        MinimumSize = new Size(850, 600);
        StartPosition = FormStartPosition.CenterParent;
        BuildUi();
        PopulateModels();
    }

    private void BuildUi()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 52,
            Padding = new Padding(12, 8, 12, 6),
            WrapContents = false,
            AutoScroll = true
        };

        _modelSelector.Width = 300;
        _modelSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _modelSelector.SelectedIndexChanged += (_, _) => DisplaySelected();

        var backup = new Button { Text = "Backup DB", AutoSize = true };
        backup.Click += (_, _) => BackupDatabase();

        var restore = new Button { Text = "Restore DB", AutoSize = true };
        restore.Click += (_, _) => RestoreDatabase();

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => ReloadHistory();

        var decreaseFont = new Button { Text = "A−", Width = 42, AccessibleName = "Decrease information font size" };
        decreaseFont.Click += (_, _) => ChangeFontSize(-1);

        var increaseFont = new Button { Text = "A+", Width = 42, AccessibleName = "Increase information font size" };
        increaseFont.Click += (_, _) => ChangeFontSize(1);

        var resetFont = new Button { Text = "Reset Font", AutoSize = true };
        resetFont.Click += (_, _) => SetFontSize(10);

        _fontSizeLabel.Text = "Font: 10 pt";
        _fontSizeLabel.AutoSize = true;
        _fontSizeLabel.Padding = new Padding(4, 7, 4, 0);

        toolbar.Controls.AddRange(new Control[] { _modelSelector, backup, restore, refresh, decreaseFont, increaseFont, resetFont, _fontSizeLabel });

        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.Padding = new Padding(12, 5, 12, 0);

        _browser.Dock = DockStyle.Fill;
        _browser.ScriptErrorsSuppressed = true;
        _browser.AllowWebBrowserDrop = false;
        _browser.IsWebBrowserContextMenuEnabled = true;
        _browser.WebBrowserShortcutsEnabled = true;

        Controls.Add(_browser);
        Controls.Add(_status);
        Controls.Add(toolbar);
    }

    private void ChangeFontSize(int delta) => SetFontSize(_fontSize + delta);

    private void SetFontSize(float size)
    {
        _fontSize = Math.Clamp(size, 8f, 24f);
        _fontSizeLabel.Text = $"Font: {_fontSize:0} pt";
        if (_browser.Document?.Body is not null)
            _browser.Document.Body.Style = $"font-size:{_fontSize:0.##}pt !important;";
    }

    private void PopulateModels()
    {
        _modelSelector.Items.Clear();
        var identities = _models.Select(m => Identity(m.Publisher, m.Name, m.Tag))
            .Concat(_history.Select(h => Identity(h.Publisher, h.Name, h.Tag)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var identity in identities) _modelSelector.Items.Add(identity);

        var preferred = ModelInformationSelectionFeature.SelectedModel;
        var preferredIdentity = preferred is null ? null : Identity(preferred.Publisher, preferred.Name, preferred.Tag);
        var preferredIndex = preferredIdentity is null ? -1 : _modelSelector.Items.IndexOf(preferredIdentity);

        if (preferredIndex >= 0)
            _modelSelector.SelectedIndex = preferredIndex;
        else if (_modelSelector.Items.Count > 0)
            _modelSelector.SelectedIndex = 0;
        else
            ShowFallback("No model information is currently stored.");
    }

    private void DisplaySelected()
    {
        if (_modelSelector.SelectedItem is not string identity) return;
        var (publisher, name, tag) = ParseIdentity(identity);
        var model = _models.FirstOrDefault(m =>
            string.Equals(NormalizePublisher(m.Publisher), publisher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeTag(m.Tag), tag, StringComparison.OrdinalIgnoreCase));
        var found = model?.Installed == true;

        var history = _history.Where(h =>
            string.Equals(NormalizePublisher(h.Publisher), publisher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeTag(h.Tag), tag, StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.AddedUtc).ToList();

        if (history.Count == 0)
        {
            ShowFallback($"Model: {name}:{tag}\r\nPublisher: {publisher}\r\nStatus: {(found ? "Found" : "Missing")}\r\n\r\nNo preserved model information is available.");
            return;
        }

        var pages = history.Where(h => !string.IsNullOrWhiteSpace(h.OfflineHtml)).ToList();
        if (pages.Count > 0)
        {
            _browser.DocumentText = BuildCombinedOfflineHtml(name, tag, pages);
            _browser.DocumentCompleted += BrowserDocumentCompleted;
        }
        else
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Model: {name}:{tag}");
            builder.AppendLine($"Publisher: {publisher}");
            builder.AppendLine($"Status: {(found ? "Found" : "Missing")}");
            builder.AppendLine();
            for (var i = 0; i < history.Count; i++)
            {
                if (i > 0)
                    builder.AppendLine().AppendLine(new string('-', 90)).AppendLine()
                        .AppendLine($"Update on {history[i].AddedUtc.ToLocalTime():dd/MM/yy}").AppendLine();
                builder.AppendLine(history[i].InformationText.Trim());
            }
            ShowFallback(builder.ToString());
        }

        _status.Text = found
            ? "Model status: Found — information is available offline."
            : "Model status: Missing — preserved information is available offline.";
    }

    private void BrowserDocumentCompleted(object? sender, WebBrowserDocumentCompletedEventArgs e)
    {
        _browser.DocumentCompleted -= BrowserDocumentCompleted;
        SetFontSize(_fontSize);
    }

    private string BuildCombinedOfflineHtml(string name, string tag, IReadOnlyList<ModelInformation> pages)
    {
        var bodies = new StringBuilder();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var content = ExtractReadablePageContent(page.OfflineHtml);
            bodies.Append($"<section class=\"snapshot\"><div class=\"snapshot-title\">Information captured on {page.AddedUtc.ToLocalTime():dd/MM/yy}</div>{content}</section>");
            if (i < pages.Count - 1) bodies.Append("<hr class=\"history-separator\">");
        }

        return $"<!doctype html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\"><style>body{{margin:0;padding:20px;background:#fff;color:#111;font-family:Segoe UI,Arial,sans-serif;font-size:{_fontSize:0.##}pt;line-height:1.45;}} .page{{max-width:1000px;margin:0 auto;}} .model-title{{font-size:1.5em;font-weight:700;margin:0 0 18px 0;padding-bottom:12px;border-bottom:2px solid #444;}} .snapshot{{margin-bottom:18px;}} .snapshot-title{{font-size:1.05em;font-weight:700;padding:8px 10px;margin-bottom:16px;border-bottom:1px solid #999;background:#f3f3f3;}} .history-separator{{border:0;border-top:2px solid #777;margin:28px 0;}} h1,h2,h3,h4{{line-height:1.25;margin-top:1.2em;}} p{{margin:0 0 12px 0;}} table{{border-collapse:collapse;max-width:100%;}} th,td{{border:1px solid #bbb;padding:6px 8px;vertical-align:top;}} pre{{white-space:pre-wrap;overflow:auto;}} code{{font-family:Consolas,monospace;}} img{{max-width:100%;height:auto;}} a{{color:#0645ad;}} ul,ol{{padding-left:28px;}} nav,header,footer,aside{{max-width:100%;}} .page-nav{{display:none;}}</style></head><body><div class=\"page\"><div class=\"model-title\">{System.Net.WebUtility.HtmlEncode(name)}:{System.Net.WebUtility.HtmlEncode(tag)}</div>{bodies}</div></body></html>";
    }

    private static string ExtractReadablePageContent(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var value = html;
        value = Regex.Replace(value, @"<script\b[^>]*>.*?</script>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<noscript\b[^>]*>.*?</noscript>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<nav\b[^>]*>.*?</nav>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<footer\b[^>]*>.*?</footer>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<header\b[^>]*>.*?</header>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<aside\b[^>]*>.*?</aside>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var main = Regex.Match(value, @"<main\b[^>]*>(?<content>.*?)</main>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (main.Success) return main.Groups["content"].Value;

        var article = Regex.Match(value, @"<article\b[^>]*>(?<content>.*?)</article>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (article.Success) return article.Groups["content"].Value;

        var body = Regex.Match(value, @"<body\b[^>]*>(?<content>.*?)</body>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return body.Success ? body.Groups["content"].Value : value;
    }

    private void ShowFallback(string text)
    {
        var escaped = System.Net.WebUtility.HtmlEncode(text).Replace("\r\n", "<br>").Replace("\n", "<br>");
        _browser.DocumentText = $"<!doctype html><html><body style=\"margin:18px;font-family:Consolas,monospace;line-height:1.45;\">{escaped}</body></html>";
        _browser.DocumentCompleted += BrowserDocumentCompleted;
    }

    private void ReloadHistory()
    {
        _history.Clear();
        _history.AddRange(_database.LoadAll());
        PopulateModels();
    }

    private void BackupDatabase()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Backup Model Information Database",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            FileName = $"OllamaModelExplorer_ModelInformation_{DateTime.Now:dd-MM-yyyy}.db",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _database.BackupTo(dialog.FileName);
            MessageBox.Show(this, "The complete model-information history, including offline page snapshots, was backed up successfully.", "Database backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Database backup error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RestoreDatabase()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Restore Model Information Database",
            Filter = "SQLite database (*.db)|*.db|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var answer = MessageBox.Show(this,
            "Restoring will replace the current model-information database. An automatic safety backup of the current database will be created first. Continue?",
            "Restore database", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (answer != DialogResult.Yes) return;
        try
        {
            _database.RestoreFrom(dialog.FileName);
            _history.Clear();
            _history.AddRange(_database.LoadAll());
            PopulateModels();
            MessageBox.Show(this, "The model-information database was restored successfully. Found/Missing status is based on the models currently known to this PC.", "Database restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Restore failed. The current database was preserved." + Environment.NewLine + Environment.NewLine + ex.Message,
                "Database restore error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Identity(string? publisher, string? name, string? tag) =>
        $"{NormalizePublisher(publisher)}/{name}:{NormalizeTag(tag)}";

    private static (string Publisher, string Name, string Tag) ParseIdentity(string identity)
    {
        var slash = identity.IndexOf('/');
        var publisher = slash > 0 ? identity[..slash] : "library";
        var modelTag = slash > 0 ? identity[(slash + 1)..] : identity;
        var colon = modelTag.LastIndexOf(':');
        var name = colon > 0 ? modelTag[..colon] : modelTag;
        var tag = colon > 0 ? modelTag[(colon + 1)..] : "latest";
        return (publisher, name, tag);
    }

    private static string NormalizePublisher(string? value) => string.IsNullOrWhiteSpace(value) ? "library" : value.Trim();
    private static string NormalizeTag(string? value) => string.IsNullOrWhiteSpace(value) ? "latest" : value.Trim();
}
