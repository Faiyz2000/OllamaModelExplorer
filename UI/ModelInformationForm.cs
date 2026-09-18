using System.Text;
using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.UI;

public sealed class ModelInformationForm : Form
{
    private readonly ModelInformationDatabase _database;
    private readonly List<ModelInfo> _models;
    private readonly List<ModelInformation> _history;
    private readonly TextBox _text = new();
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
        Width = 1000;
        Height = 700;
        MinimumSize = new Size(800, 550);
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

        toolbar.Controls.AddRange(new Control[]
        {
            _modelSelector, backup, restore, refresh,
            decreaseFont, increaseFont, resetFont, _fontSizeLabel
        });

        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.Padding = new Padding(12, 5, 12, 0);

        _text.Dock = DockStyle.Fill;
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.Font = new Font("Consolas", _fontSize, FontStyle.Regular);
        _text.BackColor = SystemColors.Window;

        Controls.Add(_text);
        Controls.Add(_status);
        Controls.Add(toolbar);
    }

    private void ChangeFontSize(int delta)
    {
        SetFontSize(_fontSize + delta);
    }

    private void SetFontSize(float size)
    {
        _fontSize = Math.Clamp(size, 8f, 24f);
        var oldFont = _text.Font;
        _text.Font = new Font(oldFont.FontFamily, _fontSize, oldFont.Style);
        oldFont.Dispose();
        _fontSizeLabel.Text = $"Font: {_fontSize:0} pt";
    }

    private void PopulateModels()
    {
        _modelSelector.Items.Clear();
        var identities = _models.Select(m => $"{m.Publisher}/{m.Name}:{m.Tag}")
            .Concat(_history.Select(h => $"{h.Publisher}/{h.Name}:{h.Tag}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var identity in identities) _modelSelector.Items.Add(identity);
        if (_modelSelector.Items.Count > 0) _modelSelector.SelectedIndex = 0;
        else _text.Text = "No model information is currently stored.";
    }

    private void DisplaySelected()
    {
        if (_modelSelector.SelectedItem is not string identity) return;
        var parts = identity.Split('/', 2);
        var publisher = parts.Length == 2 ? parts[0] : "library";
        var modelTag = parts.Length == 2 ? parts[1] : parts[0];
        var lastColon = modelTag.LastIndexOf(':');
        var name = lastColon > 0 ? modelTag[..lastColon] : modelTag;
        var tag = lastColon > 0 ? modelTag[(lastColon + 1)..] : "latest";
        var model = _models.FirstOrDefault(m =>
            string.Equals(m.Publisher, publisher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(string.IsNullOrWhiteSpace(m.Tag) ? "latest" : m.Tag, tag, StringComparison.OrdinalIgnoreCase));
        var found = model?.Installed == true;
        var history = _history.Where(h =>
            string.Equals(h.Publisher, publisher, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(h.Tag, tag, StringComparison.OrdinalIgnoreCase))
            .OrderBy(h => h.AddedUtc).ToList();

        var builder = new StringBuilder();
        builder.AppendLine($"Model: {name}:{tag}");
        builder.AppendLine($"Publisher: {publisher}");
        builder.AppendLine($"Status: {(found ? "Found" : "Missing")}");
        builder.AppendLine();
        if (history.Count == 0) builder.AppendLine("No preserved model information is available.");
        else
        {
            for (var i = 0; i < history.Count; i++)
            {
                if (i > 0)
                    builder.AppendLine().AppendLine(new string('-', 90)).AppendLine()
                        .AppendLine($"Update on {history[i].AddedUtc.ToLocalTime():dd/MM/yy}").AppendLine();
                builder.AppendLine(history[i].InformationText.Trim());
            }
        }
        _text.Text = builder.ToString();
        _status.Text = found ? "Model status: Found" : "Model status: Missing";
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
            MessageBox.Show(this, "The complete model-information history was backed up successfully.", "Database backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            MessageBox.Show(this, "The model-information database was restored successfully. Found/Missing status is based on the models currently known to this PC.",
                "Database restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Restore failed. The current database was preserved." + Environment.NewLine + Environment.NewLine + ex.Message,
                "Database restore error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
