namespace OllamaModelExplorer.Services;

/// <summary>
/// Ensures the font controls in the row double-click model-details dialog are visible.
/// This is a UI repair only; it performs no network or Ollama communication.
/// </summary>
public static class ModelDetailsFontToolbarInstaller
{
    private static System.Windows.Forms.Timer? _timer;

    public static void Install()
    {
        if (_timer != null) return;

        _timer = new System.Windows.Forms.Timer { Interval = 250 };
        _timer.Tick += (_, _) => EnsureDetailsToolbars();
        _timer.Start();
    }

    private static void EnsureDetailsToolbars()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.IsDisposed || !form.Visible || form == Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == "Ollama Model Explorer"))
                continue;

            // Do not alter the existing application log window or the dedicated Model Information form.
            if (form.Text.Contains("Application Log", StringComparison.OrdinalIgnoreCase) ||
                form.Text.Contains("Model Information", StringComparison.OrdinalIgnoreCase))
                continue;

            var details = FindDetailsTextBox(form);
            if (details == null) continue;

            var existingButton = FindControl<Button>(form, "Decrease model details font size");
            if (existingButton != null)
            {
                existingButton.Parent?.BringToFront();
                existingButton.Parent?.Refresh();
                continue;
            }

            AddToolbar(form, details);
        }
    }

    private static TextBox? FindDetailsTextBox(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is TextBox textBox && textBox.Multiline && textBox.ReadOnly)
                return textBox;

            var nested = FindDetailsTextBox(control);
            if (nested != null) return nested;
        }

        return null;
    }

    private static T? FindControl<T>(Control parent, string accessibleName) where T : Control
    {
        foreach (Control control in parent.Controls)
        {
            if (control is T typed && string.Equals(typed.AccessibleName, accessibleName, StringComparison.Ordinal))
                return typed;

            var nested = FindControl<T>(control, accessibleName);
            if (nested != null) return nested;
        }

        return null;
    }

    private static void AddToolbar(Form form, TextBox details)
    {
        var toolbar = new Panel
        {
            Name = "ModelDetailsFontToolbar",
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(8, 7, 8, 5),
            BackColor = SystemColors.Control
        };

        var decrease = new Button
        {
            Text = "A−",
            Width = 44,
            Height = 32,
            Location = new Point(8, 7),
            AccessibleName = "Decrease model details font size",
            TabStop = true
        };
        var increase = new Button
        {
            Text = "A+",
            Width = 44,
            Height = 32,
            Location = new Point(58, 7),
            AccessibleName = "Increase model details font size",
            TabStop = true
        };
        var reset = new Button
        {
            Text = "Reset Font",
            Width = 88,
            Height = 32,
            Location = new Point(108, 7)
        };
        var label = new Label
        {
            Text = "Font: 10 pt",
            AutoSize = true,
            Location = new Point(204, 14)
        };

        float size = details.Font.Size;
        void SetFont(float requested)
        {
            size = Math.Clamp(requested, 8f, 24f);
            var old = details.Font;
            details.Font = new Font(old.FontFamily, size, old.Style, old.Unit, old.GdiCharSet, old.GdiVerticalFont);
            old.Dispose();
            label.Text = $"Font: {size:0} pt";
        }

        decrease.Click += (_, _) => SetFont(size - 1f);
        increase.Click += (_, _) => SetFont(size + 1f);
        reset.Click += (_, _) => SetFont(10f);

        toolbar.Controls.Add(decrease);
        toolbar.Controls.Add(increase);
        toolbar.Controls.Add(reset);
        toolbar.Controls.Add(label);

        form.Controls.Add(toolbar);
        toolbar.BringToFront();
    }
}
