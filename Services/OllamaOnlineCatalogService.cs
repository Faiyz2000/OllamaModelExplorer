using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public sealed class OllamaOnlineCatalogService
{
    private const string LibraryUrl = "https://ollama.com/library";
    private const string NewestUrl = "https://ollama.com/library?sort=newest";
    private readonly HttpClient _http;
    private readonly string _cachePath;

    public OllamaOnlineCatalogService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OllamaModelExplorer/1.0");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OllamaModelExplorer");
        Directory.CreateDirectory(dir);
        _cachePath = Path.Combine(dir, "ollama-online-catalog.json");
    }

    public sealed record OnlineModel(string Publisher, string Name, string Description, string Capabilities, string OllamaUrl, DateTime SeenUtc);
    public sealed record UpdateResult(int CatalogModels, int ExistingModelsUpdated, IReadOnlyList<OnlineModel> NewlyDiscovered, DateTime UpdatedUtc);
    public sealed record CheckResult(int CatalogModels, IReadOnlyList<OnlineModel> NewModels, DateTime CheckedUtc);

    public async Task<UpdateResult> UpdateAsync(IReadOnlyList<ModelInfo> installedModels, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        progress?.Report("Connecting to Ollama.com...");
        var catalog = await DownloadCatalogAsync(cancellationToken);
        var old = LoadCache();
        var oldKeys = old.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newlyDiscovered = catalog.Where(x => !oldKeys.Contains(Key(x))).ToList();
        var updated = new List<OnlineModel>();
        int completed = 0;
        using var gate = new SemaphoreSlim(4, 4);

        var tasks = installedModels.Select(async model =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var online = await FetchModelPageAsync(model.Publisher, model.Name, cancellationToken);
                if (online is not null)
                    lock (updated) updated.Add(online with { SeenUtc = now });
                var done = Interlocked.Increment(ref completed);
                progress?.Report($"Updating Ollama.com information: {done}/{installedModels.Count}");
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);

        var merged = old.Concat(catalog.Select(x => x with { SeenUtc = now })).Concat(updated)
            .GroupBy(Key, StringComparer.OrdinalIgnoreCase).Select(g => g.Last())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        SaveCache(merged);
        progress?.Report("Ollama.com catalog update complete.");
        return new UpdateResult(catalog.Count, updated.Count, newlyDiscovered, now);
    }

    public async Task<CheckResult> CheckForNewAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report("Checking Ollama.com for new models...");
        var catalog = await DownloadCatalogAsync(cancellationToken);
        var oldKeys = LoadCache().Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;
        var newModels = catalog.Where(x => !oldKeys.Contains(Key(x))).Select(x => x with { SeenUtc = now }).ToList();
        progress?.Report($"Ollama.com returned {catalog.Count} catalog entries.");
        return new CheckResult(catalog.Count, newModels, now);
    }

    public IReadOnlyList<OnlineModel> LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return Array.Empty<OnlineModel>();
            return JsonSerializer.Deserialize<List<OnlineModel>>(File.ReadAllText(_cachePath)) ?? new List<OnlineModel>();
        }
        catch { return Array.Empty<OnlineModel>(); }
    }

    private void SaveCache(IEnumerable<OnlineModel> models)
    {
        File.WriteAllText(_cachePath, JsonSerializer.Serialize(models, new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task<List<OnlineModel>> DownloadCatalogAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(NewestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var results = new Dictionary<string, OnlineModel>(StringComparer.OrdinalIgnoreCase);
        const string linkPattern = "href=[\\\"'](?<href>/library/(?<name>[a-zA-Z0-9._-]+))[\\\"'][^>]*>(?<body>.*?)</a>";
        foreach (Match match in Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            var body = CleanHtml(match.Groups["body"].Value);
            results[name] = new OnlineModel("library", name, ExtractDescription(body, name), ExtractCapabilities(body), $"{LibraryUrl}/{name}", DateTime.UtcNow);
        }
        if (results.Count == 0) throw new InvalidOperationException("Ollama.com returned a page, but no library models could be parsed.");
        return results.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<OnlineModel?> FetchModelPageAsync(string publisher, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var path = string.IsNullOrWhiteSpace(publisher) || publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
            ? $"library/{Uri.EscapeDataString(name)}"
            : $"{Uri.EscapeDataString(publisher)}/{Uri.EscapeDataString(name)}";
        var url = $"https://ollama.com/{path}";
        try
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var description = ExtractMeta(html, "description") ?? ExtractMeta(html, "og:description") ?? "";
            return new OnlineModel(string.IsNullOrWhiteSpace(publisher) ? "library" : publisher, name,
                WebUtility.HtmlDecode(description).Trim(), ExtractCapabilities(CleanHtml(html)), url, DateTime.UtcNow);
        }
        catch { return null; }
    }

    private static string? ExtractMeta(string html, string name)
    {
        var pattern = "<meta[^>]+(?:name|property)=[\\\"']" + Regex.Escape(name) + "[\\\"'][^>]+content=[\\\"'](?<v>.*?)[\\\"']";
        var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? WebUtility.HtmlDecode(m.Groups["v"].Value) : null;
    }

    private static string ExtractDescription(string body, string name)
    {
        var text = Regex.Replace(WebUtility.HtmlDecode(body), "\\s+", " ").Trim();
        var pos = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (pos >= 0) text = text[(pos + name.Length)..].Trim();
        return text.Length > 600 ? text[..600].Trim() : text;
    }

    private static string ExtractCapabilities(string text)
    {
        var found = new List<string>();
        foreach (var capability in new[] { "vision", "tools", "thinking", "embedding", "audio", "cloud" })
            if (Regex.IsMatch(text, "\\b" + Regex.Escape(capability) + "\\b", RegexOptions.IgnoreCase)) found.Add(capability);
        return string.Join("|", found);
    }

    private static string CleanHtml(string value) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(value, "<[^>]+>", " ")), "\\s+", " ").Trim();
    private static string Key(OnlineModel x) => $"{x.Publisher}/{x.Name}";
}
