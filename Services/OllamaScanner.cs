using System.Text.Json;
using System.Text.RegularExpressions;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public sealed class OllamaScanner
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://localhost:11434/"), Timeout = TimeSpan.FromSeconds(90) };

    public async Task<List<ModelInfo>> ScanAsync(string ollamaRoot, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ollamaRoot)) throw new ArgumentException("Ollama folder is required.");
        var manifestRoot = Path.Combine(ollamaRoot, "manifests", "registry.ollama.ai");
        var blobRoot = Path.Combine(ollamaRoot, "blobs");
        if (!Directory.Exists(manifestRoot) || !Directory.Exists(blobRoot)) throw new DirectoryNotFoundException("The selected folder does not appear to be an Ollama model root.");

        using var response = await _http.GetAsync("api/tags", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!doc.RootElement.TryGetProperty("models", out var modelsElement) || modelsElement.ValueKind != JsonValueKind.Array) return new();
        var items = modelsElement.EnumerateArray().ToList();
        progress?.Report($"Ollama reported {items.Count} installed models.");

        var results = new ModelInfo[items.Count];
        using var gate = new SemaphoreSlim(4, 4);
        int completed = 0;
        await Task.WhenAll(items.Select(async (item, index) =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var model = CreateFromTags(item);
                await EnrichFromShowAsync(model, cancellationToken);
                results[index] = model;
                progress?.Report($"Reading model details: {Interlocked.Increment(ref completed)}/{items.Count}");
            }
            finally { gate.Release(); }
        }));

        return results.Where(x => x is not null).GroupBy(x => $"{x.Publisher}/{x.Name}:{x.Tag}", StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Tag, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static ModelInfo CreateFromTags(JsonElement item)
    {
        var raw = GetString(item, "name") ?? GetString(item, "model") ?? "";
        var (publisher, name, tag) = ParseIdentity(raw);
        var details = GetObject(item, "details");
        return new ModelInfo
        {
            Name = name, Publisher = publisher, Tag = tag, SizeBytes = GetInt64(item, "size"), ModifiedUtc = ParseDate(GetString(item, "modified_at")),
            ManifestPath = $"ollama-api://{publisher}/{name}:{tag}", Digest = GetString(item, "digest") ?? "", Installed = true,
            ParameterSize = FirstReal(GetString(details, "parameter_size"), GetString(details, "parameterSize")),
            Family = FirstReal(GetString(details, "family")),
            Quantization = FirstReal(GetString(details, "quantization_level"), GetString(details, "quantizationLevel")),
            Format = FirstReal(GetString(details, "format"))
        };
    }

    private async Task EnrichFromShowAsync(ModelInfo model, CancellationToken cancellationToken)
    {
        try
        {
            // IMPORTANT: for third-party models Ollama requires the publisher/name:tag identity.
            var exactModel = model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase)
                ? $"{model.Name}:{model.Tag}"
                : $"{model.Publisher}/{model.Name}:{model.Tag}";

            using var content = new StringContent(JsonSerializer.Serialize(new { model = exactModel, verbose = true }), System.Text.Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync("api/show", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            var details = GetObject(root, "details");
            var modelInfo = GetObject(root, "model_info");

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
            var context = FindNumberOrStringBySuffix(modelInfo, "context_length");
            if (!string.IsNullOrWhiteSpace(context)) model.Context = context;

            if (root.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                model.Capabilities = string.Join("|", caps.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));

            model.OllamaUrl = model.Publisher.Equals("library", StringComparison.OrdinalIgnoreCase) ? $"https://ollama.com/library/{model.Name}" : $"https://ollama.com/{model.Publisher}/{model.Name}";
            model.MetadataUpdatedUtc = DateTime.Now;
        }
        catch { }
    }

    private static JsonElement GetObject(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return default;
        foreach (var p in root.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Object) return p.Value;
        return default;
    }

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return null;
        foreach (var p in obj.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return Scalar(p.Value);
        return null;
    }

    private static long GetInt64(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object) return 0;
        foreach (var p in obj.EnumerateObject()) if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n)) return n; if (long.TryParse(Scalar(p.Value), out n)) return n; }
        return 0;
    }

    private static string? FindStringByKey(JsonElement root, string key)
    {
        var v = FindValueByKey(root, key); return v.ValueKind == JsonValueKind.Undefined ? null : Scalar(v);
    }

    private static JsonElement FindValueByKey(JsonElement root, string key)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject()) { if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) return p.Value; var n = FindValueByKey(p.Value, key); if (n.ValueKind != JsonValueKind.Undefined) return n; }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) { var n = FindValueByKey(item, key); if (n.ValueKind != JsonValueKind.Undefined) return n; }
        }
        return default;
    }

    private static double? FindNumberByKey(JsonElement root, string key)
    {
        var v = FindValueByKey(root, key);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    private static string? FindNumberOrStringBySuffix(JsonElement root, string suffix)
    {
        if (root.ValueKind == JsonValueKind.Object) { foreach (var p in root.EnumerateObject()) { if (p.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return Scalar(p.Value); var n = FindNumberOrStringBySuffix(p.Value, suffix); if (!string.IsNullOrWhiteSpace(n)) return n; } }
        else if (root.ValueKind == JsonValueKind.Array) foreach (var item in root.EnumerateArray()) { var n = FindNumberOrStringBySuffix(item, suffix); if (!string.IsNullOrWhiteSpace(n)) return n; }
        return null;
    }

    private static string? Scalar(JsonElement v) => v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.ToString(), JsonValueKind.True => "true", JsonValueKind.False => "false", _ => null };

    private static string MapFileTypeValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) return "";
        if (value.ValueKind == JsonValueKind.String) { var s = value.GetString() ?? ""; return s.Contains('Q', StringComparison.OrdinalIgnoreCase) ? s.ToUpperInvariant() : ""; }
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code) ? MapGgmlFileType(code) : "";
    }

    private static string InferQuantization(string raw)
    {
        var m = Regex.Match(raw, @"(?<q>Q(?:[0-8]|[I][Q])[0-9A-Z_]*|IQ[0-9A-Z_]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["q"].Value.ToUpperInvariant() : "";
    }

    private static string MapGgmlFileType(int code) => code switch
    {
        0=>"F32",1=>"F16",2=>"Q4_0",3=>"Q4_1",6=>"Q5_0",7=>"Q5_1",8=>"Q8_0",9=>"Q8_1",10=>"Q2_K",11=>"Q3_K_S",12=>"Q3_K_M",13=>"Q3_K_L",14=>"Q4_K_S",15=>"Q4_K_M",16=>"Q5_K_S",17=>"Q5_K_M",18=>"Q6_K",19=>"Q8_K",20=>"IQ2_XXS",21=>"IQ2_XS",22=>"IQ3_XXS",23=>"IQ1_S",24=>"IQ4_NL",25=>"IQ3_S",26=>"IQ2_S",27=>"IQ4_XS",28=>"IQ1_M",29=>"BF16",_=>$"file_type:{code}"
    };

    private static string FormatParameterCount(double? count)
    {
        if (!count.HasValue || count.Value <= 0) return ""; var n=count.Value;
        if(n>=1_000_000_000d)return(n/1_000_000_000d).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"B";
        if(n>=1_000_000d)return(n/1_000_000d).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"M";
        if(n>=1_000d)return(n/1_000d).ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)+"K";
        return n.ToString("0",System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FirstReal(params string?[] values){foreach(var v in values)if(!string.IsNullOrWhiteSpace(v)&&!v.Equals("unknown",StringComparison.OrdinalIgnoreCase)&&!v.Equals("n/a",StringComparison.OrdinalIgnoreCase))return v.Trim();return "";}

    private static string InferParameterSize(string raw)
    {
        var matches=Regex.Matches(raw,@"(?:^|[-_:])(?<n>\d+(?:\.\d+)?)(?<u>[bBmM])(?:$|[-_])"); if(matches.Count==0)return ""; var m=matches[^1]; return m.Groups["n"].Value+m.Groups["u"].Value.ToUpperInvariant();
    }

    private static DateTime ParseDate(string? value)=>DateTime.TryParse(value,null,System.Globalization.DateTimeStyles.RoundtripKind,out var dt)?dt.ToLocalTime():DateTime.MinValue;

    private static (string Publisher,string Name,string Tag) ParseIdentity(string raw)
    {
        raw=raw.Trim(); var tag="latest"; var colon=raw.LastIndexOf(':'); var slash=raw.LastIndexOf('/');
        if(colon>slash&&colon>=0){tag=raw[(colon+1)..];raw=raw[..colon];}
        if(string.IsNullOrWhiteSpace(raw))return("library","",tag); slash=raw.LastIndexOf('/');
        if(slash<0)return("library",raw,tag); return(string.IsNullOrWhiteSpace(raw[..slash])?"library":raw[..slash],raw[(slash+1)..],tag);
    }
}
