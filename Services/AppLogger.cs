using System.Text;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Lightweight local-only application logger. No log data is sent to the network.
/// </summary>
public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OllamaModelExplorer",
        "logs");

    private static string CurrentFilePath => Path.Combine(
        DirectoryPath,
        $"OllamaModelExplorer-{DateTime.Now:yyyy-MM-dd}.log");

    public static string LogDirectory => DirectoryPath;

    public static string CurrentLogPath => CurrentFilePath;

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}";
        Write("ERROR", text);
    }

    public static void Action(string message) => Write("ACTION", message);

    public static string ReadCurrentLog()
    {
        lock (Sync)
        {
            if (!File.Exists(CurrentFilePath))
                return "";

            return File.ReadAllText(CurrentFilePath, Encoding.UTF8);
        }
    }

    public static void ClearCurrentLog()
    {
        lock (Sync)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(CurrentFilePath, "", Encoding.UTF8);
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(CurrentFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash or interrupt the application.
        }
    }
}
