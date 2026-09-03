using OllamaModelExplorer.Services;
using OllamaModelExplorer.UI;

namespace OllamaModelExplorer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var form = new MainFormOnline();
        DeleteModelFeature.Attach(form);
        RamColumnFeature.Attach(form);
        Application.Run(form);
    }
}