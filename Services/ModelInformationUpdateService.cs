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
                var index = Interlocked.Increment(ref completed) - 1;
                progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, index, models.Count, "Fetching information..."));
                var online = await _online.FetchModelInformationAsync(model, cancellationToken);
                if (online is null || string.IsNullOrWhiteSpace(online.InformationText))
                {
                    Interlocked.Increment(ref failed);
                    progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, completed, models.Count, "No usable information returned; existing history preserved."));
                    return;
                }
                var hash = ModelInformationDatabase.ComputeHash(online.InformationText);
                var key = $"{Key(model.Publisher, model.Name, model.Tag)}|{hash}";
                if (existingHashes.Contains(key))
                {
                    Interlocked.Increment(ref skipped);
                    progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, completed, models.Count, "No new information; existing history preserved."));
                    return;
                }
                lock (results) results.Add(online with { ContentHash = hash, AddedUtc = DateTime.UtcNow, Status = "Update" });
                progress?.Report(new ModelInformationUpdateProgress(model.DisplayName, completed, models.Count, "New information staged; database unchanged."));
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
        progress?.Report(new ModelInformationUpdateProgress("Complete", models.Count, models.Count,
            $"Finished. {results.Count} new update(s), {skipped} unchanged, {failed} unavailable."));
        return new UpdateBatchResult(results, skipped, failed);
    }

    private static string Key(string publisher, string name, string tag) =>
        $"{(string.IsNullOrWhiteSpace(publisher) ? "library" : publisher)}/{name}:{(string.IsNullOrWhiteSpace(tag) ? "latest" : tag)}";
}

public sealed record ModelInformationUpdateProgress(string ModelName, int Completed, int Total, string Status);
public sealed record UpdateBatchResult(IReadOnlyList<ModelInformation> Updates, int Unchanged, int Failed);
