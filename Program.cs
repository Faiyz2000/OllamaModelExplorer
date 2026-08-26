using OllamaModelExplorer.UI;

namespace OllamaModelExplorer;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainFormOnline());
    }
}
