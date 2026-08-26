using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public sealed class OllamaScanner
{
    private readonly HttpClient _http;

    public OllamaScanner()
    {
        _http = new HttpClient { BaseAddress = new Uri("http://localhost:11434/"), Timeout = TimeSpan.FromSeconds(90) };
    }

    public async Task<List<ModelInfo>> ScanAsync(string ollamaRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ollamaRoot)) throw new ArgumentException("Ollama folder is required.");
        string manifestRoot = Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai");
        string blobRoot = Path.Combine(ollamaRoot, "blobs");
        if (!Directory.Exists(manifestRoot) || !Directory.Exists(blobRoot))
            throw new DirectoryNotFoundException("The selected folder does not appear to be an Ollama model root." + Environment.NewLine + "Expected:" + Environment.NewLine + Path.Combine(ollamaRoot, "blobs") + Environment.NewLine + Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai"));

        progress?.Report("Contacting Ollama at localhost:11434...");
        using var response = await _http.GetAsync("api/tags", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var tags = await response.Content.ReadFromJsonAsync<TagsResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        if (tags?.Models is null) return new List<ModelInfo>();
        progress?.Report($"Ollama reported {tags.Models.Count} installed models.");

        var results = new ModelInfo[tags.Models.Count];
        using var gate = new SemaphoreSlim(4, 4);
        int completed = 0;
        var tasks = tags.Models.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var model = CreateFromTags(item);
                await EnrichFromShowAsync(model, cancellationToken);
                results[index] = model;
                var done = Interlocked.Increment(ref completed);
                progress?.Report($"Reading model details: {done}/{tags.Models.Count}");
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);

        return results.Where(x => x is not null)
            .GroupBy(x => $"{x.Publisher}/{x.Name}:{x.Tag}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelInfo CreateFromTags(TagModel item)
    {
        var raw = item.Name ?? item.Model ?? "";
        var (publisher, name, tag) = ParseIdentity(raw);
        return new ModelInfo
        {
            Name = name,
            Publisher = publisher,
            Tag = tag,
            SizeBytes = item.Size,
            ModifiedUtc = ParseDate(item.ModifiedAt),
            ManifestPath = $"ollama-api://{publisher}/{name}:{tag}",
            Digest = item.Digest ?? "",
            Installed = true,
            ParameterSize = First(item.Details?.ParameterSize, InferParameterSize(raw)),
            Family = item.Details?.Family ?? "",
            Quantization = item.Details?.QuantizationLevel ?? "",
            Format = item.Details?.Format ?? ""
        };
    }

    private async Task EnrichFromShowAsync(ModelInfo model, CancellationToken cancellationToken)
    {
        try
        {
            using var content = JsonContent.Create(new { model = model.DisplayName, verbose = true });
            using var response = await _http.PostAsync("api/show", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            var show = await response.Content.ReadFromJsonAsync<ShowResponse>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
            if (show is null) return;

            model.Capabilities = show.Capabilities is null ? "" : string.Join("|", show.Capabilities);
            if (show.Details is not null)
            {
                model.ParameterSize = First(model.ParameterSize, show.Details.ParameterSize);
                model.Family = First(model.Family, show.Details.Family);
                model.Quantization = First(model.Quantization, show.Details.QuantizationLevel);
                model.Format = First(model.Format, show.Details.Format);
            }

            if (show.ModelInfo is not null)
            {
                foreach (var p in show.ModelInfo)
                {
                    if (p.Key.EndsWith(".context_length", StringComparison.OrdinalIgnoreCase))
                        model.Context = Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                    if (string.IsNullOrWhiteSpace(model.ParameterSize) && p.Key.Contains("parameter", StringComparison.OrdinalIgnoreCase))
                    {
                        var candidate = Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture);
                        model.ParameterSize = First(model.ParameterSize, candidate);
                    }
                }
            }

            model.OllamaUrl = model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
                ? $"https://ollama.com/library/{model.Name}"
                : $"https://ollama.com/{model.Publisher}/{model.Name}";
            model.MetadataUpdatedUtc = DateTime.Now;
        }
        catch
        {
            // A failed /api/show must never remove an installed model.
        }
    }

    private static string First(string? current, string? replacement) => !string.IsNullOrWhiteSpace(current) ? current : replacement ?? "";

    private static string InferParameterSize(string raw)
    {
        var matches = Regex.Matches(raw, @"(?:^|[-_:])(?<n>\d+(?:\.\d+)?)(?<u>[bBmM])(?:$|[-_])");
        if (matches.Count == 0) return "";
        var m = matches[^1];
        return m.Groups["n"].Value + m.Groups["u"].Value.ToUpperInvariant();
    }

    private static DateTime ParseDate(string? value)
    {
        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)) return dt.ToLocalTime();
        return DateTime.MinValue;
    }

    private static (string Publisher, string Name, string Tag) ParseIdentity(string raw)
    {
        raw = raw.Trim();
        string tag = "latest";
        int colon = raw.LastIndexOf(':');
        int slash = raw.LastIndexOf('/');
        if (colon > slash && colon >= 0) { tag = raw[(colon + 1)..]; raw = raw[..colon]; }
        if (string.IsNullOrWhiteSpace(raw)) return ("library", "", tag);
        slash = raw.LastIndexOf('/');
        if (slash < 0) return ("library", raw, tag);
        var publisher = string.IsNullOrWhiteSpace(raw[..slash]) ? "library" : raw[..slash];
        return (publisher, raw[(slash + 1)..], tag);
    }

    private sealed class TagsResponse { public List<TagModel> Models { get; set; } = new(); }
    private sealed class TagModel
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
        public long Size { get; set; }
        public string? Digest { get; set; }
        [JsonPropertyName("modified_at")]
        public string? ModifiedAt { get; set; }
        public TagDetails? Details { get; set; }
    }
    private sealed class TagDetails
    {
        public string? Format { get; set; }
        public string? Family { get; set; }
        [JsonPropertyName("parameter_size")]
        public string? ParameterSize { get; set; }
        [JsonPropertyName("quantization_level")]
        public string? QuantizationLevel { get; set; }
    }
    private sealed class ShowResponse
    {
        public ShowDetails? Details { get; set; }
        [JsonPropertyName("model_info")]
        public Dictionary<string, JsonElement>? ModelInfo { get; set; }
        public List<string>? Capabilities { get; set; }
    }
    private sealed class ShowDetails
    {
        public string? Format { get; set; }
        public string? Family { get; set; }
        [JsonPropertyName("parameter_size")]
        public string? ParameterSize { get; set; }
        [JsonPropertyName("quantization_level")]
        public string? QuantizationLevel { get; set; }
    }
}
