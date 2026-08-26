namespace OllamaModelExplorer.Models;

public sealed class ModelInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Tag { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime ModifiedUtc { get; set; }
    public string ManifestPath { get; set; } = "";
    public string Digest { get; set; } = "";
    public bool Installed { get; set; }
    public string Description { get; set; } = "";
    public string ParameterSize { get; set; } = "";
    public string Family { get; set; } = "";
    public string Quantization { get; set; } = "";
    public string Format { get; set; } = "";
    public string Context { get; set; } = "";
    public string CategoryText { get; set; } = "";
    public string Capabilities { get; set; } = "";
    public string OllamaUrl { get; set; } = "";
    public DateTime? MetadataUpdatedUtc { get; set; }
    public bool NewOnOllama { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Tag) ? Name : $"{Name}:{Tag}";
}
