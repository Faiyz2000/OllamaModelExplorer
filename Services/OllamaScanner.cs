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
            ParameterSize = CleanMetadata(item.Details?.ParameterSize),
            Family = CleanMetadata(item.Details?.Family),
            Quantization = CleanMetadata(item.Details?.QuantizationLevel),
            Format = CleanMetadata(item.Details?.Format)
        };
    }

    private async Task EnrichFromShowAsync(ModelInfo model, CancellationToken cancellationToken)
    {
        try
        {
            // Use the exact Ollama model identity, including publisher/tag.
            using var content = JsonContent.Create(new { model = model.DisplayName, verbose = true });
            using var response = await _http.PostAsync("api/show", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;

            var details = GetObject(root, "details");
            var modelInfo = GetObject(root, "model_info");

            model.ParameterSize = FirstReal(
                model.ParameterSize,
                GetString(details, "parameter_size"),
                GetString(details, "parameterSize"),
                FindParameterSize(modelInfo),
                InferParameterSize(model.DisplayName));

            model.Quantization = FirstReal(
                model.Quantization,
                GetString(details, "quantization_level"),
                GetString(details, "quantizationLevel"),
                FindQuantization(modelInfo));

            model.Family = FirstReal(model.Family, GetString(details, "family"));
            model.Format = FirstReal(model.Format, GetString(details, "format"));

            var context = FindNumericOrString(modelInfo, "context_length");
            if (!string.IsNullOrWhiteSpace(context)) model.Context = context;

            if (root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
            {
                model.Capabilities = string.Join("|", caps.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
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

    private static JsonElement GetObject(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return default;
        foreach (var p in root.EnumerateObject())
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object)
                return p.Value;
        return default;
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.ToString(),
                _ => null
            };
        }
        return null;
    }

    private static string? FindNumericOrString(JsonElement obj, string suffix)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.Number || p.Value.ValueKind == JsonValueKind.String)
                return p.Value.ToString();
        }
        return null;
    }

    private static string FindParameterSize(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return "";
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.EndsWith("parameter_count", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind != JsonValueKind.Number && p.Value.ValueKind != JsonValueKind.String) continue;
            if (double.TryParse(p.Value.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var count))
                return FormatParameterCount(count);
        }
        return "";
    }

    private static string FindQuantization(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object) return "";
        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.EndsWith("file_type", StringComparison.OrdinalIgnoreCase)) continue;
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var text = p.Value.GetString() ?? "";
                if (text.Contains("Q", StringComparison.OrdinalIgnoreCase)) return text.ToUpperInvariant();
            }
            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var code))
                return MapGgmlFileType(code);
        }
        return "";
    }

    private static string MapGgmlFileType(int code) => code switch
    {
        0 => "F32", 1 => "F16", 2 => "Q4_0", 3 => "Q4_1",
        6 => "Q5_0", 7 => "Q5_1", 8 => "Q8_0", 9 => "Q8_1",
        10 => "Q2_K", 11 => "Q3_K_S", 12 => "Q3_K_M", 13 => "Q3_K_L",
        14 => "Q4_K_S", 15 => "Q4_K_M", 16 => "Q5_K_S", 17 => "Q5_K_M",
        18 => "Q6_K", 19 => "Q8_K", 20 => "IQ2_XXS", 21 => "IQ2_XS",
        22 => "IQ3_XXS", 23 => "IQ1_S", 24 => "IQ4_NL", 25 => "IQ3_S",
        26 => "IQ2_S", 27 => "IQ4_XS", 28 => "IQ1_M", 29 => "BF16",
        _ => $"file_type:{code}"
    };

    private static string FormatParameterCount(double count)
    {
        if (count >= 1_000_000_000d) return (count / 1_000_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "B";
        if (count >= 1_000_000d) return (count / 1_000_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "M";
        if (count >= 1_000d) return (count / 1_000d).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "K";
        return count.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CleanMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var v = value.Trim();
        return v.Equals("unknown", StringComparison.OrdinalIgnoreCase) || v.Equals("n/a", StringComparison.OrdinalIgnoreCase) ? "" : v;
    }

    private static string FirstReal(params string?[] values)
    {
        foreach (var value in values)
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) && !value.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                return value.Trim();
        return "";
    }

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
}
