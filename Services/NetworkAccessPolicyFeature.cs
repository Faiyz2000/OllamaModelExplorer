using OllamaModelExplorer.Services;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Enforces the application's network-access rule at the user-interface boundary.
/// The only user action permitted to contact the Internet is the explicit model
/// information update. Local communication with the Ollama service remains allowed
/// for normal local model operations such as scanning and deletion.
/// </summary>
public static class NetworkAccessPolicyFeature
{
    public static void Attach(Form form)
    {
        if (form is null) throw new ArgumentNullException(nameof(form));

        foreach (var button in FindControls<Button>(form))
        {
            if (!string.Equals(button.Text, "Check for New", StringComparison.OrdinalIgnoreCase))
                continue;

            // The previous Check for New action queried Ollama.com independently.
            // Disable it so the explicit Model Information Update action remains the
            // only user action that can access the Internet.
            button.Enabled = false;
            button.Text = "Check for New (Use Update)";
            button.ToolTipIfSupported(
                "Online access is restricted to Update Model Information. Run the update to refresh the Ollama.com catalog.");
        }
    }

    private static IEnumerable<T> FindControls<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match)
                yield return match;

            foreach (var nested in FindControls<T>(child))
                yield return nested;
        }
    }

    private static void ToolTipIfSupported(this Button button, string text)
    {
        // WinForms ToolTip is intentionally created only for this disabled control.
        // It does not perform any network operation.
        var toolTip = new ToolTip();
        toolTip.SetToolTip(button, text);
    }
}
