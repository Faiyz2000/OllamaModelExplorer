using OllamaModelExplorer.Data;
using OllamaModelExplorer.Models;

namespace OllamaModelExplorer.Services;

public sealed class ModelInformationUpdateService
{
    private readonly OllamaOnlineCatalogService _online;

    public ModelInformationUpdateService(OllamaOnlineCatalogService online) => _online = online;

    public async Task<UpdateBatchResult> FetchAsync(IReadOnlyList<ModelInfo> models, IReadOnlyList<ModelInformation> existing,
        IProgress<ModelInformationUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var results = new List<ModelInformation>();
        var skipped = 0;
        var failed = 0;
        var completed = 0;
        var existingHashes = existing.Select(x => $"{Key(x.Publisher, x.Name, x.Tag)}|{x.ContentHash}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(4, 4);
        var tasks = models.Select(async model =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var online = await _online.FetchModelInformationAsync(model, cancellationToken);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, done - 1, models.Count, "Fetching exact Ollama model page..."));
                if (online is null || (string.IsNullOrWhiteSpace(online.InformationText) && string.IsNullOrWhiteSpace(online.OfflineHtml)))
                {
                    Interlocked.Increment(ref failed);
                    progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, done, models.Count, "No usable page returned; existing history preserved."));
                    return;
                }

                var comparisonSource = string.IsNullOrWhiteSpace(online.OfflineHtml) ? online.InformationText : online.OfflineHtml;
                var hash = ModelInformationDatabase.ComputeHash(comparisonSource);
                var key = $"{Key(model.Publisher, model.Name, model.Tag)}|{hash}";
                if (existingHashes.Contains(key))
                {
                    Interlocked.Increment(ref skipped);
                    progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, done, models.Count, "No new page information; existing history preserved."));
                    return;
                }

                lock (results) results.Add(online with { ContentHash = hash, AddedUtc = DateTime.UtcNow, Status = "Update" });
                progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, done, models.Count, "New page snapshot staged; database unchanged."));
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        progress?.Report(new ModelInformationUpdateProgress("Complete", models.Count, models.Count,
            $"Finished. {results.Count} new snapshot(s), {skipped} unchanged, {failed} unavailable."));
        return new UpdateBatchResult(results, skipped, failed);
    }

    private static string Key(string publisher, string name, string tag) =>
        $"{(string.IsNullOrWhiteSpace(publisher) ? "library" : publisher)}/{name}:{(string.IsNullOrWhiteSpace(tag) ? "latest" : tag)}";
}

public sealed record ModelInformationUpdateProgress(string ModelName, int Completed, int Total, string Status);
public sealed record UpdateBatchResult(IReadOnlyList<ModelInformation> Updates, int Unchanged, int Failed);
