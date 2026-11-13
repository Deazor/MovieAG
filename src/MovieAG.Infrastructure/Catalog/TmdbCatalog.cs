using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Infrastructure.Catalog;

/// <summary>
/// Метаданные фильмов и сериалов из TMDB: IMDb ID переводится во внутренний ID через /find.
/// Выборка по жанрам идёт через /discover, а для карточек каждой записи подбирается IMDb ID.
/// Фильмы и сериалы отличаются только путями и названиями полей дат, см. <see cref="TmdbMedia"/>.
/// </summary>
public sealed class TmdbCatalog(HttpClient http, SourcesConfigStore config, IMemoryCache memory, TimeProvider time)
    : IMovieCatalog, IMovieBrowser, ISeriesCatalog, ISeriesBrowser
{
    private const string Language = "ru-RU";

    /// <summary>TMDB не отдаёт страницы дальше 500-й.</summary>
    private const int MaxTmdbPage = 500;

    /// <summary>По рейтингу без порога наверх попадают записи с одной-двумя оценками.</summary>
    private const int MinRatingVotes = 200;

    /// <summary>Сколько запросов external_ids идёт параллельно: TMDB ограничивает частоту запросов.</summary>
    private const int ExternalIdsConcurrency = 6;

    /// <summary>С меньшим числом голосов оценка TMDB слишком случайна, чтобы её показывать.</summary>
    private const int MinVotesToShow = 10;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan BrowseTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExternalIdsTtl = TimeSpan.FromDays(7);
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private static readonly TmdbMedia Movies = new("movie", "primary_release_date", "primary_release_year", "фильмов");
    private static readonly TmdbMedia Tv = new("tv", "first_air_date", "first_air_date_year", "сериалов");

    private MovieCatalogDefinition Settings => config.Current.Movies.Catalog;

    // ── Фильмы ───────────────────────────────────────────────

    public async Task<MovieTitle?> GetAsync(string imdbId, CancellationToken ct)
    {
        var details = await FindDetailsAsync(Movies, imdbId, ct);
        return details is null ? null : ToMovie(imdbId, details);
    }

    public Task<IReadOnlyList<Core.Genre>> GetGenresAsync(CancellationToken ct) => GetGenresAsync(Movies, ct);

    public Task<BrowsePage<MovieTitle>> BrowseAsync(BrowseQuery query, CancellationToken ct) =>
        BrowseAsync(Movies, query, (imdbId, item) => new MovieTitle
        {
            ImdbId = imdbId,
            Name = item.DisplayName ?? imdbId,
            OriginalName = item.OriginalDisplayName,
            Year = ParseYear(item.Date),
            Description = Blank(item.Overview),
            PosterUrl = ToPosterUrl(item.PosterPath),
            Ratings = TmdbRating(item.VoteAverage, item.VoteCount)
        }, ct);

    // ── Сериалы ──────────────────────────────────────────────

    async Task<SeriesTitle?> ISeriesCatalog.GetAsync(string imdbId, CancellationToken ct)
    {
        var details = await FindDetailsAsync(Tv, imdbId, ct);
        return details is null ? null : ToSeries(imdbId, details);
    }

    public Task<IReadOnlyList<Core.Genre>> GetSeriesGenresAsync(CancellationToken ct) => GetGenresAsync(Tv, ct);

    public Task<BrowsePage<SeriesTitle>> BrowseSeriesAsync(BrowseQuery query, CancellationToken ct) =>
        BrowseAsync(Tv, query, (imdbId, item) => new SeriesTitle
        {
            ImdbId = imdbId,
            Name = item.DisplayName ?? imdbId,
            OriginalName = item.OriginalDisplayName,
            Year = ParseYear(item.Date),
            Description = Blank(item.Overview),
            PosterUrl = ToPosterUrl(item.PosterPath),
            Ratings = TmdbRating(item.VoteAverage, item.VoteCount)
        }, ct);

    // ── Общее ────────────────────────────────────────────────

    /// <summary>Без токена каталог молчит: карточки строятся из данных источников.</summary>
    private async Task<Details?> FindDetailsAsync(TmdbMedia media, string imdbId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Settings.Token)) return null;

        var key = $"tmdb:{media.Segment}:{imdbId}";
        if (memory.TryGetValue(key, out Details? cached) && cached is not null) return cached;

        var found = await GetAsync<FindResponse>($"/3/find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id", ct);
        var match = (media == Tv ? found?.TvResults : found?.MovieResults)?.FirstOrDefault();
        if (match is null) return null;

        var details = await GetAsync<Details>($"/3/{media.Segment}/{match.Id.ToString(CultureInfo.InvariantCulture)}", ct);
        if (details is not null) memory.Set(key, details, CacheTtl);
        return details;
    }

    private async Task<IReadOnlyList<Core.Genre>> GetGenresAsync(TmdbMedia media, CancellationToken ct)
    {
        RequireToken(media);

        var key = $"tmdb:genres:{media.Segment}";
        if (memory.TryGetValue(key, out IReadOnlyList<Core.Genre>? cached) && cached is not null) return cached;

        var response = await GetAsync<GenreList>($"/3/genre/{media.Segment}/list", ct);
        IReadOnlyList<Core.Genre> genres = (response?.Genres ?? [])
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new Core.Genre(g.Id.ToString(CultureInfo.InvariantCulture), Capitalize(g.Name!)))
            .OrderBy(g => g.Name, StringComparer.Create(RussianCulture, ignoreCase: true))
            .ToList();

        memory.Set(key, genres, CacheTtl);
        return genres;
    }

    /// <summary>
    /// Без текста — /discover с фильтрами и сортировкой на стороне TMDB.
    /// С текстом — /search: он не умеет фильтровать по жанру и сортировать, поэтому это делается внутри страницы,
    /// и страница может оказаться короче обычной.
    /// </summary>
    private async Task<BrowsePage<T>> BrowseAsync<T>(
        TmdbMedia media, BrowseQuery query, Func<string, ListedItem, T> map, CancellationToken ct)
    {
        RequireToken(media);

        var key = ($"tmdb:browse:{media.Segment}", query with { Text = query.Text?.Trim().ToLowerInvariant() });
        if (memory.TryGetValue(key, out BrowsePage<T>? cached) && cached is not null) return cached;

        var hasText = !string.IsNullOrWhiteSpace(query.Text);
        var list = await GetAsync<ItemList>(hasText ? BuildSearchPath(media, query) : BuildDiscoverPath(media, query), ct);

        var results = list?.Results ?? [];
        if (hasText) results = SortWithinPage(results.Where(m => Matches(m, query)), query.Sort);

        var items = await ResolveImdbIdsAsync(media, results, ct);
        var hasMore = list is not null && query.Page < Math.Min(list.TotalPages, MaxTmdbPage);

        var page = new BrowsePage<T>(items.Select(x => map(x.ImdbId, x.Item)).ToList(), query.Page, hasMore);
        memory.Set(key, page, BrowseTtl);
        return page;
    }

    private string BuildDiscoverPath(TmdbMedia media, BrowseQuery query)
    {
        var parameters = new List<string>
        {
            "include_adult=false",
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"sort_by={ToSortBy(media, query.Sort)}"
        };

        if (!string.IsNullOrEmpty(query.Genre)) parameters.Add($"with_genres={Uri.EscapeDataString(query.Genre)}");
        if (query.YearFrom is { } from) parameters.Add($"{media.DateField}.gte={from.ToString(CultureInfo.InvariantCulture)}-01-01");

        // По дате выхода без верхней границы первыми шли бы ещё не вышедшие записи.
        var to = query.YearTo is { } year ? new DateOnly(year, 12, 31) : (DateOnly?)null;
        if (query.Sort == BrowseSort.Date)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            to = to is null || to > today ? today : to;
        }

        if (to is { } end) parameters.Add($"{media.DateField}.lte={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (query.Sort == BrowseSort.Rating) parameters.Add($"vote_count.gte={MinRatingVotes.ToString(CultureInfo.InvariantCulture)}");

        return $"/3/discover/{media.Segment}?" + string.Join('&', parameters);
    }

    private static string BuildSearchPath(TmdbMedia media, BrowseQuery query)
    {
        var path = $"/3/search/{media.Segment}?include_adult=false&page={query.Page.ToString(CultureInfo.InvariantCulture)}&query={Uri.EscapeDataString(query.Text!.Trim())}";
        return query.YearFrom is { } from && from == query.YearTo
            ? $"{path}&{media.YearParam}={from.ToString(CultureInfo.InvariantCulture)}"
            : path;
    }

    private static string ToSortBy(TmdbMedia media, BrowseSort sort) => sort switch
    {
        BrowseSort.Rating => "vote_average.desc",
        BrowseSort.Popularity => "popularity.desc",
        _ => $"{media.DateField}.desc"
    };

    private static bool Matches(ListedItem item, BrowseQuery query)
    {
        if (!string.IsNullOrEmpty(query.Genre)
            && !(item.GenreIds ?? []).Any(id => id.ToString(CultureInfo.InvariantCulture) == query.Genre))
        {
            return false;
        }

        var year = ParseYear(item.Date);
        if (query.YearFrom is { } from && (year is null || year < from)) return false;
        return query.YearTo is not { } to || (year is not null && year <= to);
    }

    private static List<ListedItem> SortWithinPage(IEnumerable<ListedItem> items, BrowseSort sort) => sort switch
    {
        BrowseSort.Rating => items.OrderByDescending(m => m.VoteAverage ?? 0).ToList(),
        BrowseSort.Popularity => items.OrderByDescending(m => m.Popularity ?? 0).ToList(),
        _ => items.OrderByDescending(m => m.Date ?? "", StringComparer.Ordinal).ToList()
    };

    /// <summary>Записи без IMDb ID пропускаются: на страницах фильмов и сериалов это ключ карточки.</summary>
    private async Task<IReadOnlyList<(string ImdbId, ListedItem Item)>> ResolveImdbIdsAsync(
        TmdbMedia media, IReadOnlyList<ListedItem> items, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(ExternalIdsConcurrency);
        var imdbIds = await Task.WhenAll(items.Select(async item =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await GetImdbIdAsync(media, item.Id, ct);
            }
            finally
            {
                throttle.Release();
            }
        }));

        return items
            .Zip(imdbIds, (item, imdbId) => (ImdbId: imdbId, Item: item))
            .Where(x => ImdbId.IsValid(x.ImdbId))
            .DistinctBy(x => x.ImdbId)
            .Select(x => (x.ImdbId!, x.Item))
            .ToList();
    }

    private async Task<string?> GetImdbIdAsync(TmdbMedia media, int tmdbId, CancellationToken ct)
    {
        var id = tmdbId.ToString(CultureInfo.InvariantCulture);
        var key = $"tmdb:imdb:{media.Segment}:{id}";
        if (memory.TryGetValue(key, out string? cached)) return cached;

        var ids = await GetAsync<ExternalIds>($"/3/{media.Segment}/{id}/external_ids", ct);
        var imdbId = string.IsNullOrEmpty(ids?.ImdbId) ? null : ids.ImdbId;
        memory.Set(key, imdbId, ExternalIdsTtl);
        return imdbId;
    }

    private MovieTitle ToMovie(string imdbId, Details details) => new()
    {
        ImdbId = imdbId,
        Name = details.DisplayName ?? imdbId,
        OriginalName = details.OriginalDisplayName,
        Year = ParseYear(details.Date),
        RuntimeMinutes = details.Runtime is > 0 ? details.Runtime : null,
        Description = Blank(details.Overview),
        PosterUrl = ToPosterUrl(details.PosterPath),
        Genres = details.Genres?.Select(g => g.Name).OfType<string>().ToList() ?? [],
        Ratings = TmdbRating(details.VoteAverage, details.VoteCount)
    };

    private SeriesTitle ToSeries(string imdbId, Details details) => new()
    {
        ImdbId = imdbId,
        Name = details.DisplayName ?? imdbId,
        OriginalName = details.OriginalDisplayName,
        Year = ParseYear(details.Date),
        SeasonsCount = details.NumberOfSeasons is > 0 ? details.NumberOfSeasons : null,
        Description = Blank(details.Overview),
        PosterUrl = ToPosterUrl(details.PosterPath),
        Genres = details.Genres?.Select(g => g.Name).OfType<string>().ToList() ?? [],
        Ratings = TmdbRating(details.VoteAverage, details.VoteCount)
    };

    private string? ToPosterUrl(string? path) =>
        string.IsNullOrEmpty(path) ? null : Settings.ImagesUrl.TrimEnd('/') + path;

    private void RequireToken(TmdbMedia media)
    {
        if (string.IsNullOrEmpty(Settings.Token))
        {
            throw new CatalogUnavailableException($"Для выбора {media.GenitivePlural} по жанрам задайте токен TMDB в переменной TMDB_TOKEN");
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        var settings = Settings;
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{settings.Url.TrimEnd('/')}{path}{separator}language={Language}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        Authorize(request, settings.Token!);

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(SourceJson.Options, ct);
    }

    /// <summary>TMDB выдаёт два вида ключей: токен чтения (JWT) идёт в заголовок, короткий API-ключ — в адрес.</summary>
    private static void Authorize(HttpRequestMessage request, string token)
    {
        if (token.Contains('.', StringComparison.Ordinal))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return;
        }

        request.RequestUri = new Uri($"{request.RequestUri}&api_key={Uri.EscapeDataString(token)}");
    }

    private static IReadOnlyList<Rating> TmdbRating(double? average, int? votes) =>
        votes >= MinVotesToShow && Rating.Create(Rating.Tmdb, average) is { } rating ? [rating] : [];

    private static int? ParseYear(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var parsed) ? parsed.Year : null;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    /// <summary>TMDB на русском пишет жанры со строчной буквы: «мелодрама».</summary>
    private static string Capitalize(string name) =>
        name.Length == 0 ? name : char.ToUpper(name[0], RussianCulture) + name[1..];

    /// <summary>Чем фильмы отличаются от сериалов в адресах TMDB.</summary>
    private sealed record TmdbMedia(string Segment, string DateField, string YearParam, string GenitivePlural);

    private sealed record FindResponse
    {
        public List<FindMatch>? MovieResults { get; init; }
        public List<FindMatch>? TvResults { get; init; }
    }

    private sealed record FindMatch(int Id);

    /// <summary>Поля фильма (title, release_date) и сериала (name, first_air_date) в одной записи.</summary>
    private abstract record TmdbItem
    {
        public string? Title { get; init; }
        public string? Name { get; init; }
        public string? OriginalTitle { get; init; }
        public string? OriginalName { get; init; }
        public string? ReleaseDate { get; init; }
        public string? FirstAirDate { get; init; }
        public string? Overview { get; init; }
        public string? PosterPath { get; init; }
        public double? VoteAverage { get; init; }
        public int? VoteCount { get; init; }

        public string? DisplayName => Title ?? Name ?? OriginalTitle ?? OriginalName;
        public string? OriginalDisplayName => OriginalTitle ?? OriginalName;
        public string? Date => ReleaseDate ?? FirstAirDate;
    }

    private sealed record Details : TmdbItem
    {
        public int? Runtime { get; init; }
        public int? NumberOfSeasons { get; init; }
        public List<GenreName>? Genres { get; init; }
    }

    /// <summary>Запись /discover и /search: метаданные без IMDb ID.</summary>
    private sealed record ListedItem : TmdbItem
    {
        public int Id { get; init; }
        public List<int>? GenreIds { get; init; }
        public double? Popularity { get; init; }
    }

    private sealed record GenreName(string? Name);

    private sealed record GenreList(List<GenreItem>? Genres);

    private sealed record GenreItem(int Id, string? Name);

    private sealed record ItemList(List<ListedItem>? Results, int TotalPages);

    private sealed record ExternalIds(string? ImdbId);
}
