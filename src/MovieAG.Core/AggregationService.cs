using Microsoft.Extensions.Logging;

namespace MovieAG.Core;

/// <summary>Опрашивает источники параллельно; сбой одного не ломает выдачу остальных.</summary>
public sealed class AggregationService(
    ISourceProvider sources,
    ICatalog catalog,
    ILogger<AggregationService> logger)
{
    public async Task<SearchResult> SearchAsync(string query, CancellationToken ct)
    {
        var active = sources.Active;
        var catalogTask = SafeCatalogAsync(c => c.SearchAsync(query, ct), ct);
        var (found, failed) = await QueryAllAsync(active, (s, token) => s.SearchAsync(query, token), ct);
        var hits = await catalogTask;

        var cards = await MergeWithCatalogAsync(hits, found.SelectMany(r => r).ToList(), ct);
        return new SearchResult(cards, active.Select(s => s.Id).ToList(), failed);
    }

    public async Task<IReadOnlyList<TitleCard>> GetFreshAsync(int limit, CancellationToken ct)
    {
        var (found, _) = await QueryAllAsync(sources.Active, (s, token) => s.GetLatestAsync(limit, token), ct);
        var cards = await MergeWithCatalogAsync([], found.SelectMany(r => r).ToList(), ct);
        return cards.Take(limit).ToList();
    }

    public async Task<TitleDetails?> GetTitleAsync(int id, CancellationToken ct)
    {
        var title = await catalog.GetAsync(id, ct);
        if (title is null) return null;

        var (variants, failed) = await QueryAllAsync(sources.Active, (s, token) => s.GetVariantsAsync(title, token), ct);
        return new TitleDetails(title, TitleMerger.GroupDubs(variants.SelectMany(v => v)), failed);
    }

    public async Task<Playback?> GetPlaybackAsync(int id, string sourceId, string dubId, int episode, CancellationToken ct)
    {
        var source = sources.Active.FirstOrDefault(s => s.Id == sourceId);
        if (source is null) return null;

        var title = await catalog.GetAsync(id, ct);
        if (title is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceFanOut.SourceTimeout);
        return await source.GetPlaybackAsync(title, dubId, episode, timeout.Token);
    }

    private async Task<IReadOnlyList<TitleCard>> MergeWithCatalogAsync(
        IReadOnlyList<CatalogTitle> hits,
        IReadOnlyList<SourceTitle> found,
        CancellationToken ct)
    {
        var known = hits.Select(h => h.Id).ToHashSet();
        var missing = found.Select(f => f.ShikimoriId).Where(id => !known.Contains(id)).Distinct().ToList();
        var extra = missing.Count == 0 ? [] : await SafeCatalogAsync(c => c.GetManyAsync(missing, ct), ct);
        return TitleMerger.MergeCards([.. hits, .. extra], found);
    }

    private async Task<IReadOnlyList<CatalogTitle>> SafeCatalogAsync(
        Func<ICatalog, Task<IReadOnlyList<CatalogTitle>>> call,
        CancellationToken ct)
    {
        try
        {
            return await call(catalog);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Без каталога карточки строятся из данных источников.
            logger.LogWarning(ex, "Каталог недоступен");
            return [];
        }
    }

    private Task<(IReadOnlyList<T> Results, IReadOnlyList<string> Failed)> QueryAllAsync<T>(
        IReadOnlyList<IAnimeSource> active,
        Func<IAnimeSource, CancellationToken, Task<T>> call,
        CancellationToken ct) =>
        SourceFanOut.QueryAllAsync(active, call, logger, ct);
}
