using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

/// <summary>
/// Derives professional model-intelligence fields from Ollama metadata.
/// Runtime-engine features are deliberately labelled as backend capabilities
/// rather than pretending they are model-native properties.
/// </summary>
public static class ModelCapabilityFeature
{
    public static void Apply(ModelInfo model)
    {
        if (model is null) throw new ArgumentNullException(nameof(model));

        model.Architecture = First(model.Architecture, model.Family, "Unknown");
        model.StorageFormat = First(model.StorageFormat, model.Format, "Unknown");

        var isGguf = model.StorageFormat.Equals("gguf", StringComparison.OrdinalIgnoreCase);
        model.GgufLoader = isGguf ? "Supported" : "N/A";

        // Current Ollama's llama.cpp runner uses file-backed/mmap model loading,
        // but Ollama can disable mmap under host-memory pressure. Therefore the
        // column reports the backend behaviour rather than claiming a fixed state.
        model.DiskBackedTensorAccess = isGguf ? "Backend-managed (mmap)" : "Backend-dependent";
        model.MemoryBudgetManager = "Ollama-managed";
        model.ArchitectureSupport = model.Installed
            ? "Confirmed by Ollama"
            : "Not verified locally";

        model.MoeExpertStreaming = DetectMoe(model)
            ? "MoE detected; streaming API not exposed"
            : "N/A";

        model.InputModality = BuildInputModality(model.Capabilities);
    }

    private static bool DetectMoe(ModelInfo model)
    {
        var architecture = model.Architecture ?? "";
        var family = model.Family ?? "";
        var text = $"{architecture} {family} {model.Capabilities}";
        return text.Contains("moe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mixture", StringComparison.OrdinalIgnoreCase)
            || text.Contains("expert", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildInputModality(string capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
            return "Text";

        var caps = capabilities
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToHashSet();

        var modalities = new List<string>();
        if (caps.Contains("vision") || caps.Contains("image"))
            modalities.Add("Image");
        if (caps.Contains("audio") || caps.Contains("speech"))
            modalities.Add("Audio");
        if (caps.Contains("video"))
            modalities.Add("Video");

        if (modalities.Count == 0)
            return caps.Contains("embedding") ? "Text / Embedding" : "Text";

        modalities.Add("Text");
        return modalities.Count > 2 ? "Multimodal" : string.Join(" + ", modalities);
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
