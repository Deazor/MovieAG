using System.Globalization;
using System.Text.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Сериалы из балансера Alloha, воспроизведение через iframe. Нужен собственный токен из кабинета Alloha.
/// Фильмы пропускаются: сериалом считается запись с сезонами. У каждой озвучки свой набор серий.
/// </summary>
public sealed class AllohaSeriesSource(HttpClient http, SeriesSourceDefinition definition, TimeProvider time) : ISeriesSource
{
    public const string AdapterName = "alloha";

    /// <summary>Вариант без названия озвучки: общий плеер серии.</summary>
    public const string DefaultStreamId = "default";

    /// <summary>Как и у фильмов, новинками считаются сериалы, начавшиеся в этом или прошлом году.</summary>
    private const int RecentYears = 1;

    /// <summary>«Во все тяжкие»: проверка связи лишь убеждается, что Alloha принимает токен.</summary>
    private const string PingImdbId = "tt0903747";

    private readonly AllohaApiClient _api = new(http, definition);

    public string Id => definition.Id;

    public async Task<IReadOnlyList<SeriesTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var items = await _api.SearchAsync(query, ct);
        return items
            .Where(IsSeries)
            .GroupBy(i => i.IdImdb!, StringComparer.Ordinal)
            .Select(g => ToTitle(g.First()))
            .ToList();
    }

    public async Task<IReadOnlyList<SeriesTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        var minYear = time.GetUtcNow().Year - RecentYears;
        var items = await _api.GetLatestAsync("serial", definition.Latest ?? limit, i => IsSeries(i) && i.Year >= minYear, ct);
        return items.Select(ToTitle).ToList();
    }

    public async Task<SourceSeries?> FindAsync(string imdbId, CancellationToken ct)
    {
        var item = await FindSeriesAsync(imdbId, ct);
        if (item is null) return null;

        var streams = ListStreams(ReadSeasons(item));
        return streams.Count == 0 ? null : new SourceSeries(ToTitle(item), streams);
    }

    public async Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, int season, int episode, CancellationToken ct)
    {
        var item = await FindSeriesAsync(imdbId, ct);
        if (item is null) return null;

        var found = ReadSeasons(item)
            .FirstOrDefault(e => e.Season == season && e.Number == episode);
        if (found is null) return null;

        var link = streamId == DefaultStreamId
            ? found.Iframe
            : found.Translations.GetValueOrDefault(streamId)?.Iframe;
        return string.IsNullOrEmpty(link) ? null : new Playback(PlaybackKind.Iframe, AllohaApiClient.NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => _api.FindByImdbAsync(PingImdbId, ct);

    private async Task<AllohaItem?> FindSeriesAsync(string imdbId, CancellationToken ct)
    {
        if (!ImdbId.IsValid(imdbId)) return null;

        var items = await _api.FindByImdbAsync(imdbId, ct);
        return items.FirstOrDefault(i => IsSeries(i) && i.IdImdb == imdbId);
    }

    private static bool IsSeries(AllohaItem item) => AllohaApiClient.IsSeries(item) && ImdbId.IsValid(item.IdImdb);

    /// <summary>Одна озвучка — один вариант просмотра с сериями всех сезонов, где она есть.</summary>
    private List<SeriesStream> ListStreams(IReadOnlyList<EpisodeEntry> episodes)
    {
        var streams = episodes
            .SelectMany(e => e.Translations
                .Where(t => !string.IsNullOrEmpty(t.Value.Iframe))
                .Select(t => (StreamId: t.Key, Name: t.Value.DisplayName, Episode: new SeriesEpisode(e.Season, e.Number))))
            .GroupBy(x => x.StreamId, StringComparer.Ordinal)
            .Select(g => new SeriesStream(
                Id,
                g.Key,
                g.Select(x => x.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? "Без названия",
                Sorted(g.Select(x => x.Episode))))
            .ToList();

        // Если озвучки у серий не перечислены, остаётся общий плеер каждой серии.
        var withoutTranslations = episodes.Where(e => e.Translations.Count == 0 && !string.IsNullOrEmpty(e.Iframe)).ToList();
        if (streams.Count == 0 && withoutTranslations.Count > 0)
        {
            streams.Add(new SeriesStream(Id, DefaultStreamId, "Alloha", Sorted(withoutTranslations.Select(e => new SeriesEpisode(e.Season, e.Number)))));
        }

        return streams;
    }

    private static List<SeriesEpisode> Sorted(IEnumerable<SeriesEpisode> episodes) =>
        episodes.Distinct().OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();

    /// <summary>
    /// Разворачивает seasons → episodes → translation в плоский список серий.
    /// Номер сезона и серии берётся из ключа; записи с нечисловыми ключами пропускаются.
    /// </summary>
    private static List<EpisodeEntry> ReadSeasons(AllohaItem item)
    {
        Dictionary<string, AllohaSeason>? seasons;
        try
        {
            seasons = item.Seasons?.Deserialize<Dictionary<string, AllohaSeason>>(SourceJson.Options);
        }
        catch (JsonException)
        {
            return [];
        }

        var entries = new List<EpisodeEntry>();
        foreach (var (seasonKey, season) in seasons ?? [])
        {
            if (!int.TryParse(seasonKey, CultureInfo.InvariantCulture, out var seasonNumber)) continue;

            foreach (var (episodeKey, episode) in season.Episodes ?? [])
            {
                if (!int.TryParse(episodeKey, CultureInfo.InvariantCulture, out var episodeNumber)) continue;
                entries.Add(new EpisodeEntry(seasonNumber, episodeNumber, episode.Iframe, episode.Translation ?? []));
            }
        }

        return entries;
    }

    private static SeriesTitle ToTitle(AllohaItem item) => new()
    {
        ImdbId = item.IdImdb!,
        Name = item.Name ?? item.OriginalName ?? item.IdImdb!,
        OriginalName = item.OriginalName,
        Year = item.Year,
        SeasonsCount = item.SeasonsCount,
        Description = item.Description,
        PosterUrl = item.Poster,
        Genres = AllohaApiClient.Genres(item),
        Ratings = AllohaApiClient.Ratings(item)
    };

    private sealed record EpisodeEntry(int Season, int Number, string? Iframe, Dictionary<string, AllohaTranslation> Translations);

    private sealed record AllohaSeason(Dictionary<string, AllohaEpisode>? Episodes);

    private sealed record AllohaEpisode(string? Iframe, Dictionary<string, AllohaTranslation>? Translation);
}
