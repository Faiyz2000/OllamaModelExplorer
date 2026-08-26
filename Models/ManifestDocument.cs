using System.Text.Json;

namespace OllamaModelExplorer.Models;

public sealed class ManifestDocument
{
    public int SchemaVersion { get; set; }
    public string? MediaType { get; set; }
    public string? ConfigDigest { get; set; }
    public List<ManifestLayer> Layers { get; set; } = new();

    public static bool TryParse(string path, out ManifestDocument? manifest)
    {
        manifest = null;
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return false;

            // Ollama manifests are JSON files without a .json extension.
            // We intentionally identify them by their manifest structure, not by filename.
            bool looksLikeManifest =
                root.TryGetProperty("schemaVersion", out _) &&
                (root.TryGetProperty("layers", out var layers) && layers.ValueKind == JsonValueKind.Array);

            if (!looksLikeManifest)
                return false;

            manifest = JsonSerializer.Deserialize<ManifestDocument>(
                root.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return manifest != null;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class ManifestLayer
{
    public string? MediaType { get; set; }
    public string? Digest { get; set; }
    public long Size { get; set; }
}
