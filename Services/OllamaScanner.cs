using System.Net.Http.Json;
using System.Text.Json;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Uses Ollama's local API as the authoritative source of installed models.
/// The filesystem is only validated as the selected Ollama storage root.
/// This is important because the manifest tree is not a reliable inventory:
/// Ollama can report installed models that cannot be reconstructed correctly
/// by walking manifests alone.
/// </summary>
public sealed class OllamaScanner
{
    private readonly HttpClient _http;

    public OllamaScanner()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434/"),
            Timeout = TimeSpan.FromSeconds(90)
        };
    }

    public async Task<List<ModelInfo>> ScanAsync(
        string ollamaRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ollamaRoot))
            throw new ArgumentException("Ollama folder is required.");

        string manifestRoot = Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai");
        string blobRoot = Path.Combine(ollamaRoot, "blobs");

        if (!Directory.Exists(manifestRoot) || !Directory.Exists(blobRoot))
        {
            throw new DirectoryNotFoundException(
                "The selected folder does not appear to be an Ollama model root." +
                Environment.NewLine +
                "Expected:" + Environment.NewLine +
                Path.Combine(ollamaRoot, "blobs") + Environment.NewLine +
                Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai"));
        }

        progress?.Report("Contacting Ollama at localhost:11434...");

        using var response = await _http.GetAsync(
            "api/tags",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var tags = await response.Content.ReadFromJsonAsync<TagsResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (tags?.Models is null)
            return new List<ModelInfo>();

        progress?.Report($"Ollama reported {tags.Models.Count} installed models.");

        // /api/tags already contains the essential model metadata. /api/show
        // is used to enrich each row, but an individual failure never removes
        // the model from the result.
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

                int done = Interlocked.Increment(ref completed);
                progress?.Report($"Reading model details: {done}/{tags.Models.Count}");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results
            .Where(x => x is not null)
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
            // Stable synthetic identity. This avoids requiring a particular
            // manifest layout and makes every /api/tags model persistable.
            ManifestPath = $"ollama-api://{publisher}/{name}:{tag}",
            Digest = item.Digest ?? "",
            Installed = true,
            ParameterSize = item.Details?.ParameterSize ?? "",
            Family = item.Details?.Family ?? "",
            Quantization = item.Details?.QuantizationLevel ?? "",
            Format = item.Details?.Format ?? ""
        };
    }

    private async Task EnrichFromShowAsync(
        ModelInfo model,
        CancellationToken cancellationToken)
    {
        try
        {
            using var content = JsonContent.Create(new { model = model.DisplayName });
            using var response = await _http.PostAsync(
                "api/show", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return;

            var show = await response.Content.ReadFromJsonAsync<ShowResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            if (show is null)
                return;

            model.Description = "";
            model.Capabilities = show.Capabilities is null
                ? ""
                : string.Join("|", show.Capabilities);

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
                    {
                        model.Context = Convert.ToString(p.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
                        break;
                    }
                }
            }

            // Keep a useful local Ollama URL even for community namespaces.
            model.OllamaUrl = $"https://ollama.com/library/{model.Name}";
            model.MetadataUpdatedUtc = DateTime.Now;
        }
        catch
        {
            // /api/tags is authoritative. A failed /api/show request must
            // never cause an installed model to disappear from the grid.
        }
    }

    private static string First(string current, string? replacement) =>
        !string.IsNullOrWhiteSpace(current) ? current : replacement ?? "";

    private static DateTime ParseDate(string? value)
    {
        if (DateTime.TryParse(
            value,
            null,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var dt))
            return dt.ToLocalTime();

        return DateTime.MinValue;
    }

    private static (string Publisher, string Name, string Tag) ParseIdentity(string raw)
    {
        raw = raw.Trim();

        string tag = "latest";
        int colon = raw.LastIndexOf(':');
        int slash = raw.LastIndexOf('/');

        if (colon > slash && colon >= 0)
        {
            tag = raw[(colon + 1)..];
            raw = raw[..colon];
        }

        if (string.IsNullOrWhiteSpace(raw))
            return ("library", "", tag);

        slash = raw.LastIndexOf('/');
        if (slash < 0)
            return ("library", raw, tag);

        var publisher = raw[..slash];
        var name = raw[(slash + 1)..];

        if (string.IsNullOrWhiteSpace(publisher))
            publisher = "library";

        return (publisher, name, tag);
    }

    private sealed class TagsResponse
    {
        public List<TagModel> Models { get; set; } = new();
    }

    private sealed class TagModel
    {
        public string? Name { get; set; }
        public string? Model { get; set; }
        public long Size { get; set; }
        public string? Digest { get; set; }
        public string? ModifiedAt { get; set; }
        public TagDetails? Details { get; set; }
    }

    private sealed class TagDetails
    {
        public string? Format { get; set; }
        public string? Family { get; set; }
        public string? ParameterSize { get; set; }
        public string? QuantizationLevel { get; set; }
    }

    private sealed class ShowResponse
    {
        public ShowDetails? Details { get; set; }
        public Dictionary<string, JsonElement>? ModelInfo { get; set; }
        public List<string>? Capabilities { get; set; }
    }

    private sealed class ShowDetails
    {
        public string? Format { get; set; }
        public string? Family { get; set; }
        public string? ParameterSize { get; set; }
        public string? QuantizationLevel { get; set; }
    }
}
