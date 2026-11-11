using Microsoft.Extensions.Logging;

namespace MovieAG.Core;

/// <summary>Собирает фильмы из источников и дополняет их данными каталога.</summary>
public sealed class MovieService(
    IMovieSourceProvider sources,
    IMovieCatalog catalog,
    ILogger<MovieService> logger)
{
    /// <summary>Сколько карточек фильмов отдаётся за один запрос списка.</summary>
    public const int MaxCards = 40;

    public Task<MovieListResult> SearchAsync(string query, CancellationToken ct) =>
        ListAsync((source, token) => source.SearchAsync(query, token), ct);

    public Task<MovieListResult> GetLatestAsync(int limit, CancellationToken ct) =>
        ListAsync((source, token) => source.GetLatestAsync(limit, token), ct);

    public async Task<MovieDetails?> GetAsync(string imdbId, CancellationToken ct)
    {
        var (found, failed) = await SourceFanOut.QueryAllAsync(
            sources.Active,
            (source, token) => source.FindAsync(imdbId, token),
            logger,
            ct);

        var movies = found.OfType<SourceMovie>().ToList();
        var info = await SafeCatalogAsync(imdbId, ct);
        var title = movies.FirstOrDefault()?.Title.EnrichedBy(info) ?? info;

        return title is null ? null : new MovieDetails(title, movies.SelectMany(m => m.Streams).ToList(), failed);
    }

    public async Task<Playback?> GetPlaybackAsync(string imdbId, string sourceId, string streamId, CancellationToken ct)
    {
        var source = sources.Active.FirstOrDefault(s => s.Id == sourceId);
        if (source is null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(SourceFanOut.SourceTimeout);
        return await source.GetPlaybackAsync(imdbId, streamId, timeout.Token);
    }

    private async Task<MovieListResult> ListAsync(
        Func<IMovieSource, CancellationToken, Task<IReadOnlyList<MovieTitle>>> call,
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
            .Take(MaxCards);

        var cards = await Task.WhenAll(groups.Select(group => ToCardAsync(group.ToList(), ct)));
        return new MovieListResult(cards, active.Select(s => s.Id).ToList(), failed);
    }

    private async Task<MovieCard> ToCardAsync(IReadOnlyList<(string SourceId, MovieTitle Title)> hits, CancellationToken ct)
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

    private async Task<MovieTitle?> SafeCatalogAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            return await catalog.GetAsync(imdbId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Без каталога карточка строится из данных источника.
            logger.LogWarning(ex, "Каталог фильмов недоступен");
            return null;
        }
    }
}
