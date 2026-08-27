using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.UI;

public sealed class LogForm : Form
{
    private readonly TextBox _text = new();

    public LogForm()
    {
        Text = "Ollama Model Explorer - Application Log";
        StartPosition = FormStartPosition.CenterParent;
        Width = 1100;
        Height = 700;
        MinimumSize = new Size(700, 450);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 46,
            Padding = new Padding(8),
            WrapContents = false
        };

        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RefreshLog();

        var openNotepad = new Button { Text = "Open in Notepad", AutoSize = true };
        openNotepad.Click += (_, _) =>
        {
            try
            {
                if (!File.Exists(AppLogger.CurrentLogPath))
                    AppLogger.Info("Log viewer created the daily log file.");

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{AppLogger.CurrentLogPath}\"",
                    UseShellExecute = true
                });
                AppLogger.Action("Opened application log in Windows Notepad.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Unable to open the log in Notepad.", ex);
                MessageBox.Show(this, ex.Message, "Log", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        var clear = new Button { Text = "Clear Current Log", AutoSize = true };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Clear today's application log?", "Clear log",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            AppLogger.ClearCurrentLog();
            AppLogger.Action("Current application log was cleared by the user.");
            RefreshLog();
        };

        var path = new Label
        {
            AutoSize = true,
            Padding = new Padding(10, 7, 0, 0),
            Text = AppLogger.CurrentLogPath
        };

        toolbar.Controls.Add(refresh);
        toolbar.Controls.Add(openNotepad);
        toolbar.Controls.Add(clear);
        toolbar.Controls.Add(path);

        _text.Dock = DockStyle.Fill;
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.ScrollBars = ScrollBars.Both;
        _text.WordWrap = false;
        _text.Font = new Font("Consolas", 10);
        _text.BackColor = SystemColors.Window;

        Controls.Add(_text);
        Controls.Add(toolbar);
        RefreshLog();
    }

    private void RefreshLog()
    {
        _text.Text = AppLogger.ReadCurrentLog();
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }
}
