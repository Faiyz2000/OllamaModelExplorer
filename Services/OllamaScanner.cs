using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public sealed class OllamaScanner
{
    private readonly HttpClient _http = new()
    {
        BaseAddress = new Uri("http://localhost:11434/"),
        Timeout = TimeSpan.FromSeconds(90)
    };

    public async Task<List<ModelInfo>> ScanAsync(
        string ollamaRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ollamaRoot))
            throw new ArgumentException("Ollama folder is required.", nameof(ollamaRoot));

        var manifestRoot = Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai");
        var blobRoot = Path.Combine(ollamaRoot, "blobs");
        if (!Directory.Exists(manifestRoot) || !Directory.Exists(blobRoot))
            throw new DirectoryNotFoundException(
                "The selected folder does not appear to be an Ollama model root. Select the folder containing 'blobs' and 'manifests'.");

        // /api/tags is the authoritative local inventory. Metadata enrichment must
        // never be allowed to remove or hide an installed model.
        progress?.Report("Reading the complete Ollama inventory...");
        using var response = await _http.GetAsync(
            "api/tags", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("models", out var modelsElement) ||
            modelsElement.ValueKind != JsonValueKind.Array)
            return new List<ModelInfo>();

        var items = modelsElement.EnumerateArray().ToList();
        progress?.Report($"Ollama reported {items.Count} installed models.");

        var results = new ModelInfo?[items.Count];
        using var gate = new SemaphoreSlim(4, 4);
        var completed = 0;

        var tasks = items.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                // Creating the model from /api/tags must always succeed independently
                // of /api/show. This is what guarantees that all installed models remain visible.
                ModelInfo model;
                try
                {
                    model = CreateFromTags(item);
                }
                catch
                {
                    model = CreateMinimalModel(item);
                }

                // /api/show is optional enrichment. It is deliberately isolated so a
                // failed request, malformed response, or unsupported field cannot fail the scan.
                await EnrichFromShowAsync(model, cancellationToken);
                results[index] = model;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // As a final safety net, a model reported by /api/tags must still appear.
                try { results[index] = CreateMinimalModel(item); }
                catch { results[index] = null; }
            }
            finally
            {
                var n = Interlocked.Increment(ref completed);
                progress?.Report($"Reading model details: {n}/{items.Count}");
                gate.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);

        return results
            .Where(x => x is not null)
            .Select(x => x!)
            .GroupBy(x => $"{x.Publisher}/{x.Name}:{x.Tag}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelInfo CreateFromTags(JsonElement item)
    {
        var raw = GetString(item, "name") ?? GetString(item, "model") ?? "";
        var identity = ParseIdentity(raw);
        var details = GetObject(item, "details");

        return new ModelInfo
        {
            Name = identity.Name,
            Publisher = identity.Publisher,
            Tag = identity.Tag,
            SizeBytes = GetInt64(item, "size"),
            ModifiedUtc = ParseDate(GetString(item, "modified_at")),
            ManifestPath = $"ollama-api://{identity.Publisher}/{identity.Name}:{identity.Tag}",
            Digest = GetString(item, "digest") ?? "",
            Installed = true,
            ParameterSize = FirstReal(
                GetString(details, "parameter_size"),
                GetString(details, "parameterSize")),
            Family = FirstReal(GetString(details, "family")),
            Quantization = FirstReal(
                GetString(details, "quantization_level"),
                GetString(details, "quantizationLevel")),
            Format = FirstReal(GetString(details, "format"))
        };
    }

    private static ModelInfo CreateMinimalModel(JsonElement item)
    {
        var raw = GetString(item, "name") ?? GetString(item, "model") ?? "unknown";
        var identity = ParseIdentity(raw);
        return new ModelInfo
        {
            Name = identity.Name,
            Publisher = identity.Publisher,
            Tag = identity.Tag,
            SizeBytes = GetInt64(item, "size"),
            ModifiedUtc = ParseDate(GetString(item, "modified_at")),
            ManifestPath = $"ollama-api://{identity.Publisher}/{identity.Name}:{identity.Tag}",
            Digest = GetString(item, "digest") ?? "",
            Installed = true
        };
    }

    private async Task EnrichFromShowAsync(ModelInfo model, CancellationToken cancellationToken)
    {
        try
        {
            var exactModel = model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
                ? $"{model.Name}:{model.Tag}"
                : $"{model.Publisher}/{model.Name}:{model.Tag}";

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/show")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { model = exactModel, verbose = true }),
                    Encoding.UTF8,
                    "application/json")
            };

            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var details = GetObject(root, "details");

            // Ollama normally exposes these values under details. Some versions/builds
            // expose additional raw values under model_info, so search the complete response.
            model.ParameterSize = FirstReal(
                model.ParameterSize,
                FindStringByKey(details, "parameter_size"),
                FindStringByKey(root, "parameter_size"),
                FormatParameterCount(FindNumberByKey(root, "parameter_count")),
                FormatParameterCount(FindNumberByKey(root, "parameter.count")),
                InferParameterSize(exactModel));

            model.Quantization = FirstReal(
                model.Quantization,
                FindStringByKey(details, "quantization_level"),
                FindStringByKey(root, "quantization_level"),
                MapFileTypeValue(FindValueByKey(root, "file_type")),
                InferQuantization(exactModel));

            model.Family = FirstReal(model.Family, FindStringByKey(details, "family"));
            model.Format = FirstReal(model.Format, FindStringByKey(details, "format"));

            var context = FindNumberOrStringBySuffix(root, "context_length");
            if (!string.IsNullOrWhiteSpace(context))
                model.Context = context;

            if (root.TryGetProperty("capabilities", out var caps) &&
                caps.ValueKind == JsonValueKind.Array)
            {
                model.Capabilities = string.Join(
                    "|",
                    caps.EnumerateArray()
                        .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            model.OllamaUrl = model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
                ? $"https://ollama.com/library/{model.Name}"
                : $"https://ollama.com/{model.Publisher}/{model.Name}";

            // Only mark metadata as updated when /api/show actually returned a valid response.
            model.MetadataUpdatedUtc = DateTime.Now;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Metadata is optional. Never allow an /api/show failure to remove a local model.
        }
    }

    private static JsonElement GetObject(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return default;

        foreach (var p in root.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                p.Value.ValueKind == JsonValueKind.Object)
                return p.Value;
        }

        return default;
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var p in obj.EnumerateObject())
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return Scalar(p.Value);
        }

        return null;
    }

    private static long GetInt64(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return 0;

        foreach (var p in obj.EnumerateObject())
        {
            if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n))
                return n;

            if (long.TryParse(Scalar(p.Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
        }

        return 0;
    }

    private static string? FindStringByKey(JsonElement root, string key)
    {
        var value = FindValueByKey(root, key);
        return value.ValueKind == JsonValueKind.Undefined ? null : Scalar(value);
    }

    private static JsonElement FindValueByKey(JsonElement root, string key)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return p.Value;

                var nested = FindValueByKey(p.Value, key);
                if (nested.ValueKind != JsonValueKind.Undefined)
                    return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindValueByKey(item, key);
                if (nested.ValueKind != JsonValueKind.Undefined)
                    return nested;
            }
        }

        return default;
    }

    private static double? FindNumberByKey(JsonElement root, string key)
    {
        var value = FindValueByKey(root, key);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;

        return null;
    }

    private static string? FindNumberOrStringBySuffix(JsonElement root, string suffix)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return Scalar(p.Value);

                var nested = FindNumberOrStringBySuffix(p.Value, suffix);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindNumberOrStringBySuffix(item, suffix);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private static string MapFileTypeValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            return "";

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? "";
            return text.Contains('Q', StringComparison.OrdinalIgnoreCase)
                ? text.ToUpperInvariant()
                : "";
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code)
            ? MapGgmlFileType(code)
            : "";
    }

    private static string InferQuantization(string raw)
    {
        var match = Regex.Match(
            raw,
            @"(?<q>(?:Q(?:[0-8]|I[Q])[0-9A-Z_]*|IQ[0-9A-Z_]+))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["q"].Value.ToUpperInvariant() : "";
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

    private static string FormatParameterCount(double? count)
    {
        if (!count.HasValue || count.Value <= 0)
            return "";

        var n = count.Value;
        if (n >= 1_000_000_000d)
            return (n / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "B";
        if (n >= 1_000_000d)
            return (n / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        if (n >= 1_000d)
            return (n / 1_000d).ToString("0.##", CultureInfo.InvariantCulture) + "K";
        return n.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FirstReal(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !value.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                !value.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                return value.Trim();
        }

        return "";
    }

    private static string InferParameterSize(string raw)
    {
        var matches = Regex.Matches(
            raw,
            @"(?:^|[-_:])(?<n>\d+(?:\.\d+)?)(?<u>[bBmM])(?:$|[-_])");
        if (matches.Count == 0)
            return "";

        var match = matches[^1];
        return match.Groups["n"].Value + match.Groups["u"].Value.ToUpperInvariant();
    }

    private static DateTime ParseDate(string? value) =>
        DateTime.TryParse(value, null, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToLocalTime()
            : DateTime.MinValue;

    private static (string Publisher, string Name, string Tag) ParseIdentity(string raw)
    {
        raw = raw.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return ("library", "unknown", "latest");

        var tag = "latest";
        var colon = raw.LastIndexOf(':');
        var slash = raw.LastIndexOf('/');
        if (colon > slash && colon >= 0)
        {
            tag = raw[(colon + 1)..];
            raw = raw[..colon];
        }

        if (string.IsNullOrWhiteSpace(raw))
            return ("library", "unknown", tag);

        slash = raw.LastIndexOf('/');
        if (slash < 0)
            return ("library", raw, tag);

        var publisher = raw[..slash];
        var name = raw[(slash + 1)..];
        return (string.IsNullOrWhiteSpace(publisher) ? "library" : publisher, name, tag);
    }
}
