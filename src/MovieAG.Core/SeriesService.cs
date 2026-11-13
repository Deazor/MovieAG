using Microsoft.Extensions.Logging;

namespace MovieAG.Core;

/// <summary>
/// Собирает сериалы из источников и дополняет их данными каталога.
/// Карточки и списки те же, что у фильмов: <see cref="MovieCard"/> и <see cref="MovieListResult"/> адресуются IMDb ID.
/// </summary>
public sealed class SeriesService(
    ISeriesSourceProvider sources,
    ISeriesCatalog catalog,
    ILogger<SeriesService> logger)
{
    public Task<MovieListResult> SearchAsync(string query, CancellationToken ct) =>
        ListAsync((source, token) => source.SearchAsync(query, token), ct);

    public Task<MovieListResult> GetLatestAsync(int limit, CancellationToken ct) =>
        ListAsync((source, token) => source.GetLatestAsync(limit, token), ct);

    public async Task<SeriesDetails?> GetAsync(string imdbId, CancellationToken ct)
    {
        var (found, failed) = await SourceFanOut.QueryAllAsync(
            sources.Active,
            (source, token) => source.FindAsync(imdbId, token),
            logger,
            ct);

        var series = found.OfType<SourceSeries>().ToList();
        var info = await SafeCatalogAsync(imdbId, ct);
        var title = series.FirstOrDefault()?.Title.EnrichedBy(info) ?? info;
        if (title is null) return null;

        var ratings = Rating.Merge([title.Ratings, .. series.Skip(1).Select(s => s.Title.Ratings)]);
        return new SeriesDetails(title with { Ratings = ratings }, series.SelectMany(s => s.Streams).ToList(), failed);
    }

    public async Task<Playback?> GetPlaybackAsync(
        string imdbId, string sourceId, string streamId, int season, int episode, CancellationToken ct)
    {
        var source = sources.Active.FirstOrDefault(s => s.Id == sourceId);
        if (source is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceFanOut.SourceTimeout);
        return await source.GetPlaybackAsync(imdbId, streamId, season, episode, timeout.Token);
    }

    private async Task<MovieListResult> ListAsync(
        Func<ISeriesSource, CancellationToken, Task<IReadOnlyList<SeriesTitle>>> call,
        CancellationToken ct)
    {
        var active = sources.Active;
        var (found, failed) = await SourceFanOut.QueryAllAsync(
            active,
            async (source, token) => (source.Id, Titles: await call(source, token)),
            logger,
            ct);

        var groups = found
            .SelectMany(result => result.Titles.Select(title => (SourceId: result.Id, Title: title)))
            .GroupBy(hit => hit.Title.ImdbId, StringComparer.Ordinal)
            .Take(MovieService.MaxCards);

        var cards = await Task.WhenAll(groups.Select(group => ToCardAsync(group.ToList(), ct)));
        return new MovieListResult(cards, active.Select(s => s.Id).ToList(), failed);
    }

    private async Task<MovieCard> ToCardAsync(IReadOnlyList<(string SourceId, SeriesTitle Title)> hits, CancellationToken ct)
    {
        var title = hits[0].Title.EnrichedBy(await SafeCatalogAsync(hits[0].Title.ImdbId, ct));
        return new MovieCard(
            title.ImdbId,
            title.Name,
            title.OriginalName,
            title.Year,
            title.PosterUrl,
            hits.Select(h => h.SourceId).Distinct().ToList(),
            Rating.Merge([title.Ratings, .. hits.Skip(1).Select(h => h.Title.Ratings)]));
    }

    private async Task<SeriesTitle?> SafeCatalogAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            return await catalog.GetAsync(imdbId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Без каталога карточка строится из данных источника.
            logger.LogWarning(ex, "Каталог сериалов недоступен");
            return null;
        }
    }
}
