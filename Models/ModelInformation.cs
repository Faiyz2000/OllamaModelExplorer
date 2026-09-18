namespace OllamaModelExplorer.Models;

public sealed record ModelInformation
{
    public long Id { get; set; }
    public string Publisher { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tag { get; set; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(Tag) ? Name : $"{Name}:{Tag}";
    public string InformationText { get; set; } = "";
    public string OfflineHtml { get; set; } = "";
    public DateTime AddedUtc { get; set; }
    public string SourceUrl { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string Status { get; set; } = "Success";
}
