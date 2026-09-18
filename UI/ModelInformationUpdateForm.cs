using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;
using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

public sealed class ModelInformationUpdateForm : Form
{
    private readonly ModelInformationDatabase _database;
    private readonly ModelInformationUpdateService _service;
    private readonly IReadOnlyList<ModelInfo> _models;
    private readonly List<ModelInformation> _existing;
    private readonly List<ModelInformation> _staged = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _status = new();
    private readonly Label _current = new();
    private readonly Button _actionButton = new();
    private CancellationTokenSource? _cts;
    private bool _completed;
    private bool _committed;

    public ModelInformationUpdateForm(ModelInformationDatabase database, ModelInformationUpdateService service,
        IReadOnlyList<ModelInfo> models, IReadOnlyList<ModelInformation> existing)
    {
        _database = database;
        _service = service;
        _existing = existing.ToList();
        _models = models.ToList();

        Text = "Model Information Update";
        Width = 720;
        Height = 250;
        MinimumSize = new Size(600, 220);
        StartPosition = FormStartPosition.CenterParent;
        FormClosing += OnFormClosing;
        BuildUi();
    }

    private void BuildUi()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 5 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _current.Text = $"Preparing {_models.Count} model(s)...";
        _current.Dock = DockStyle.Fill;
        _current.AutoEllipsis = true;
        panel.Controls.Add(_current, 0, 0);

        _progressBar.Minimum = 0;
        _progressBar.Maximum = Math.Max(1, _models.Count);
        _progressBar.Dock = DockStyle.Fill;
        panel.Controls.Add(_progressBar, 0, 1);

        _status.Text = "The main project remains usable while this window is open.";
        _status.Dock = DockStyle.Fill;
        panel.Controls.Add(_status, 0, 2);

        var note = new Label
        {
            Text = "All known models are checked, including Found and Missing models. New information is staged in memory; existing information is not replaced until this form closes.",
            Dock = DockStyle.Fill,
            AutoSize = false
        };
        panel.Controls.Add(note, 0, 3);

        _actionButton.Text = "Cancel";
        _actionButton.AutoSize = true;
        _actionButton.Anchor = AnchorStyles.Right;
        _actionButton.Click += (_, _) =>
        {
            if (_completed)
                Close();
            else
                _cts?.Cancel();
        };
        panel.Controls.Add(_actionButton, 0, 4);
        Controls.Add(panel);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await RunAsync();
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<ModelInformationUpdateProgress>(UpdateProgress);
            var result = await _service.FetchAsync(_models, _existing, progress, _cts.Token);
            _staged.AddRange(result.Updates);
            _completed = true;
            _progressBar.Value = _progressBar.Maximum;
            _actionButton.Text = "Close";
            _actionButton.Enabled = true;
            _status.Text = $"Update scan finished: {_staged.Count} new information snapshot(s), {result.Unchanged} unchanged, {result.Failed} unavailable. Close this form to commit the staged updates.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Update cancelled. Existing information will be preserved.";
            _completed = true;
            _staged.Clear();
            _actionButton.Text = "Close";
            _actionButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = "Update failed. Existing information will be preserved.";
            MessageBox.Show(this, ex.Message, "Model information update", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _completed = true;
            _staged.Clear();
            _actionButton.Text = "Close";
            _actionButton.Enabled = true;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void UpdateProgress(ModelInformationUpdateProgress p)
    {
        _current.Text = p.Total > 0 && p.Completed < p.Total
            ? $"Model: {p.ModelName} ({Math.Min(p.Completed + 1, p.Total)}/{p.Total})"
            : p.ModelName;
        _progressBar.Value = Math.Min(_progressBar.Maximum, Math.Max(0, p.Completed));
        _status.Text = p.Status;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_completed)
        {
            e.Cancel = true;
            _cts?.Cancel();
            _status.Text = "Cancellation requested. Finishing the current request...";
            return;
        }

        if (_committed || _staged.Count == 0) return;

        try
        {
            _database.AppendUpdates(_staged);
            _committed = true;
        }
        catch (Exception ex)
        {
            e.Cancel = true;
            MessageBox.Show(this,
                "The update could not be committed, so the existing information was preserved." + Environment.NewLine + Environment.NewLine + ex.Message,
                "Model information update", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
