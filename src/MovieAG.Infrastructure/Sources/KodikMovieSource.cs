using System.Globalization;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Фильмы из балансера Kodik, воспроизведение через iframe.
/// С собственным токеном: поиск по названию и IMDb ID, новинки, отдельный вариант на каждую озвучку.
/// Без токена работает только /get-player по IMDb ID, а публичный токен находит лишь аниме-фильмы;
/// тогда поиск понимает только IMDb ID, а озвучка выбирается внутри плеера.
/// </summary>
public sealed class KodikMovieSource(HttpClient http, MovieSourceDefinition definition, IKodikTokenProvider? publicTokens = null) : IMovieSource
{
    public const string AdapterName = "kodik";
    public const string PlayerStreamId = "player";
    private const string MovieTypes = "foreign-movie,russian-movie,soviet-cartoon,foreign-cartoon,russian-cartoon,anime";
    private const int MaxResults = 100;

    private readonly KodikApiClient _api = new(http, definition, publicTokens);

    public string Id => definition.Id;

    public async Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var isImdbId = ImdbId.IsValid(query);

        if (_api.UsesPublicToken)
        {
            return isImdbId && await GetPlayerAsync(query, ct) is not null ? [PlaceholderTitle(query)] : [];
        }

        var results = isImdbId
            ? await SearchByImdbIdAsync(query, ct)
            : (await QueryAsync(ct, ("title", query))).Results;
        return ToTitles(results);
    }

    public async Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        // Список новинок есть только в полном API.
        if (_api.UsesPublicToken) return [];

        var response = await _api.PostAsync("/list", ct,
            ("types", MovieTypes),
            ("sort", "updated_at"),
            ("order", "desc"),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToTitles(response.Results).Take(limit).ToList();
    }

    public async Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct)
    {
        if (!ImdbId.IsValid(imdbId)) return null;

        if (_api.UsesPublicToken)
        {
            var player = await GetPlayerAsync(imdbId, ct);
            return player is null
                ? null
                : new SourceMovie(PlaceholderTitle(imdbId), [new MovieStream(Id, PlayerStreamId, $"{player.Translation ?? "Kodik"} (озвучка в плеере)")]);
        }

        var results = await SearchByImdbIdAsync(imdbId, ct);
        if (results.Count == 0) return null;

        var streams = results
            .Where(r => r.Translation is not null && !string.IsNullOrEmpty(r.Link))
            .GroupBy(r => r.Translation!.Id)
            .Select(g => ToStream(g.First().Translation!))
            .ToList();
        return streams.Count == 0 ? null : new SourceMovie(ToTitle(results[0]), streams);
    }

    public async Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct)
    {
        if (!ImdbId.IsValid(imdbId)) return null;

        string? link;
        if (_api.UsesPublicToken)
        {
            link = streamId == PlayerStreamId ? (await GetPlayerAsync(imdbId, ct))?.Link : null;
        }
        else
        {
            link = (await SearchByImdbIdAsync(imdbId, ct))
                .FirstOrDefault(r => r.Translation?.Id.ToString(CultureInfo.InvariantCulture) == streamId)?.Link;
        }

        return string.IsNullOrEmpty(link) ? null : new Playback(PlaybackKind.Iframe, KodikApiClient.NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => _api.PingAsync(MovieTypes, ct);

    private Task<KodikPlayerResponse?> GetPlayerAsync(string imdbId, CancellationToken ct) =>
        _api.GetPlayerAsync("imdbID", imdbId, ct);

    private async Task<List<KodikResult>> SearchByImdbIdAsync(string imdbId, CancellationToken ct)
    {
        var response = await QueryAsync(ct, ("imdb_id", imdbId));
        return (response.Results ?? [])
            .Where(r => string.Equals(r.ImdbId, imdbId, StringComparison.Ordinal))
            .ToList();
    }

    private Task<KodikResponse> QueryAsync(CancellationToken ct, (string Key, string Value) filter) =>
        _api.PostAsync("/search", ct,
            filter,
            ("types", MovieTypes),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Записи без IMDb ID пропускаются: на странице фильмов это ключ карточки.</summary>
    private static List<MovieTitle> ToTitles(List<KodikResult>? results) =>
        (results ?? [])
            .Where(r => ImdbId.IsValid(r.ImdbId))
            .GroupBy(r => r.ImdbId!, StringComparer.Ordinal)
            .Select(g => ToTitle(g.First()))
            .ToList();

    private static MovieTitle ToTitle(KodikResult result) => new()
    {
        ImdbId = result.ImdbId!,
        Name = result.Title ?? result.TitleOrig ?? result.ImdbId!,
        OriginalName = result.TitleOrig,
        Year = result.Year,
        RuntimeMinutes = result.MaterialData?.Duration,
        Description = result.MaterialData?.Description,
        PosterUrl = result.MaterialData?.PosterUrl,
        Genres = result.MaterialData?.Genres ?? [],
        Ratings = new[]
            {
                Rating.Create(Rating.Imdb, result.MaterialData?.ImdbRating),
                Rating.Create(Rating.Kinopoisk, result.MaterialData?.KinopoiskRating)
            }
            .OfType<Rating>()
            .ToList()
    };

    /// <summary>/get-player не отдаёт метаданные: название и постер подставит каталог TMDB, если он настроен.</summary>
    private static MovieTitle PlaceholderTitle(string imdbId) => new() { ImdbId = imdbId, Name = imdbId };

    private MovieStream ToStream(KodikTranslation translation) => new(
        Id,
        translation.Id.ToString(CultureInfo.InvariantCulture),
        translation.Type == "subtitles" ? $"{translation.Title ?? "Субтитры"} · субтитры" : translation.Title ?? "Без названия");
}
