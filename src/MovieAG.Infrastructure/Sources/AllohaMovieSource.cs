using System.Globalization;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Фильмы из балансера Alloha (api.alloha.tv), воспроизведение через iframe. Нужен собственный токен из кабинета Alloha.
/// Ищет по IMDb ID и по названию, у фильма отдельный вариант просмотра на каждую озвучку.
/// Отдаёт новинки текущего и прошлого года; сериалы на странице фильмов пропускаются.
/// </summary>
public sealed class AllohaMovieSource(HttpClient http, MovieSourceDefinition definition, TimeProvider time) : IMovieSource
{
    public const string AdapterName = "alloha";
    public const string DefaultStreamId = "default";

    /// <summary>Сколько лет назад вышедший фильм ещё считается новинкой: 1 — этот и прошлый год.</summary>
    private const int RecentYears = 1;

    /// <summary>«Зелёная миля»: проверка связи лишь убеждается, что Alloha принимает токен.</summary>
    private const string PingImdbId = "tt0120689";

    private readonly AllohaApiClient _api = new(http, definition);

    public string Id => definition.Id;

    public async Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var items = await _api.SearchAsync(query, ct);
        return items
            .Where(IsMovie)
            .GroupBy(i => i.IdImdb!, StringComparer.Ordinal)
            .Select(g => ToTitle(g.First()))
            .ToList();
    }

    /// <summary>
    /// В начале списка Alloha бывают и старые фильмы, добавленные недавно,
    /// поэтому новинками считаются фильмы этого и прошлого года.
    /// </summary>
    public async Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        var minYear = time.GetUtcNow().Year - RecentYears;
        var items = await _api.GetLatestAsync("movie", definition.Latest ?? limit, i => IsMovie(i) && i.Year >= minYear, ct);
        return items.Select(ToTitle).ToList();
    }

    public async Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct)
    {
        var item = await FindMovieAsync(imdbId, ct);
        if (item is null) return null;

        var streams = ListStreams(item);
        return streams.Count == 0 ? null : new SourceMovie(ToTitle(item), streams);
    }

    public async Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct)
    {
        var item = await FindMovieAsync(imdbId, ct);
        if (item is null) return null;

        var link = streamId == DefaultStreamId
            ? item.Iframe
            : item.TranslationIframe?.GetValueOrDefault(streamId)?.Iframe;
        return string.IsNullOrEmpty(link) ? null : new Playback(PlaybackKind.Iframe, AllohaApiClient.NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => _api.FindByImdbAsync(PingImdbId, ct);

    private async Task<AllohaItem?> FindMovieAsync(string imdbId, CancellationToken ct)
    {
        if (!ImdbId.IsValid(imdbId)) return null;

        var items = await _api.FindByImdbAsync(imdbId, ct);
        return items.FirstOrDefault(i => IsMovie(i) && i.IdImdb == imdbId);
    }

    private static bool IsMovie(AllohaItem item) => !AllohaApiClient.IsSeries(item) && ImdbId.IsValid(item.IdImdb);

    private List<MovieStream> ListStreams(AllohaItem item)
    {
        var streams = (item.TranslationIframe ?? [])
            .Where(t => !string.IsNullOrEmpty(t.Value.Iframe))
            .Select(t => new MovieStream(Id, t.Key, AllohaApiClient.Label(t.Value.DisplayName, t.Value.Quality)))
            .ToList();

        // Если озвучки не перечислены, остаётся общий плеер фильма.
        if (streams.Count == 0 && !string.IsNullOrEmpty(item.Iframe))
        {
            streams.Add(new MovieStream(Id, DefaultStreamId, "Alloha"));
        }

        return streams;
    }

    private static MovieTitle ToTitle(AllohaItem item) => new()
    {
        ImdbId = item.IdImdb!,
        Name = item.Name ?? item.OriginalName ?? item.IdImdb!,
        OriginalName = item.OriginalName,
        Year = item.Year,
        RuntimeMinutes = ParseRuntime(item.Time),
        Description = item.Description,
        PosterUrl = item.Poster,
        Genres = AllohaApiClient.Genres(item),
        Ratings = AllohaApiClient.Ratings(item)
    };

    /// <summary>Длительность приходит строкой вида «03:09» (часы:минуты).</summary>
    private static int? ParseRuntime(string? time) =>
        TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero
            ? (int)span.TotalMinutes
            : null;
}
