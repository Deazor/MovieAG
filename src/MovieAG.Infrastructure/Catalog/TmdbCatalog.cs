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
/// Метаданные фильмов из TMDB: IMDb ID переводится во внутренний ID через /find.
/// Выборка по жанрам идёт через /discover, а для карточек каждому фильму подбирается IMDb ID.
/// </summary>
public sealed class TmdbCatalog(HttpClient http, SourcesConfigStore config, IMemoryCache memory, TimeProvider time)
    : IMovieCatalog, IMovieBrowser
{
    private const string Language = "ru-RU";

    /// <summary>TMDB не отдаёт страницы дальше 500-й.</summary>
    private const int MaxTmdbPage = 500;

    /// <summary>По рейтингу без порога наверх попадают фильмы с одной-двумя оценками.</summary>
    private const int MinRatingVotes = 200;

    /// <summary>Сколько запросов external_ids идёт параллельно: TMDB ограничивает частоту запросов.</summary>
    private const int ExternalIdsConcurrency = 6;

    /// <summary>С меньшим числом голосов оценка TMDB слишком случайна, чтобы её показывать.</summary>
    private const int MinVotesToShow = 10;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan BrowseTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ExternalIdsTtl = TimeSpan.FromDays(7);
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private MovieCatalogDefinition Settings => config.Current.Movies.Catalog;

    public async Task<IReadOnlyList<Core.Genre>> GetGenresAsync(CancellationToken ct)
    {
        RequireToken();

        const string key = "tmdb:genres";
        if (memory.TryGetValue(key, out IReadOnlyList<Core.Genre>? cached) && cached is not null) return cached;

        var response = await GetAsync<GenreList>("/3/genre/movie/list", ct);
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
    public async Task<BrowsePage<MovieTitle>> BrowseAsync(BrowseQuery query, CancellationToken ct)
    {
        RequireToken();

        var key = ("tmdb:browse", query with { Text = query.Text?.Trim().ToLowerInvariant() });
        if (memory.TryGetValue(key, out BrowsePage<MovieTitle>? cached) && cached is not null) return cached;

        var hasText = !string.IsNullOrWhiteSpace(query.Text);
        var list = await GetAsync<MovieList>(hasText ? BuildSearchPath(query) : BuildDiscoverPath(query), ct);

        var results = list?.Results ?? [];
        if (hasText) results = SortWithinPage(results.Where(m => Matches(m, query)), query.Sort);

        var titles = await ResolveImdbIdsAsync(results, ct);
        var hasMore = list is not null && query.Page < Math.Min(list.TotalPages, MaxTmdbPage);

        var page = new BrowsePage<MovieTitle>(titles, query.Page, hasMore);
        memory.Set(key, page, BrowseTtl);
        return page;
    }

    internal string BuildDiscoverPath(BrowseQuery query)
    {
        var parameters = new List<string>
        {
            "include_adult=false",
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"sort_by={ToSortBy(query.Sort)}"
        };

        if (!string.IsNullOrEmpty(query.Genre)) parameters.Add($"with_genres={Uri.EscapeDataString(query.Genre)}");
        if (query.YearFrom is { } from) parameters.Add($"primary_release_date.gte={from.ToString(CultureInfo.InvariantCulture)}-01-01");

        // По дате выхода без верхней границы первыми шли бы ещё не вышедшие фильмы.
        var to = query.YearTo is { } year ? new DateOnly(year, 12, 31) : (DateOnly?)null;
        if (query.Sort == BrowseSort.Date)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            to = to is null || to > today ? today : to;
        }

        if (to is { } end) parameters.Add($"primary_release_date.lte={end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (query.Sort == BrowseSort.Rating) parameters.Add($"vote_count.gte={MinRatingVotes.ToString(CultureInfo.InvariantCulture)}");

        return "/3/discover/movie?" + string.Join('&', parameters);
    }

    internal static string BuildSearchPath(BrowseQuery query)
    {
        var path = $"/3/search/movie?include_adult=false&page={query.Page.ToString(CultureInfo.InvariantCulture)}&query={Uri.EscapeDataString(query.Text!.Trim())}";
        return query.YearFrom is { } from && from == query.YearTo
            ? $"{path}&primary_release_year={from.ToString(CultureInfo.InvariantCulture)}"
            : path;
    }

    private static string ToSortBy(BrowseSort sort) => sort switch
    {
        BrowseSort.Rating => "vote_average.desc",
        BrowseSort.Popularity => "popularity.desc",
        _ => "primary_release_date.desc"
    };

    private static bool Matches(ListedMovie movie, BrowseQuery query)
    {
        if (!string.IsNullOrEmpty(query.Genre)
            && !(movie.GenreIds ?? []).Any(id => id.ToString(CultureInfo.InvariantCulture) == query.Genre))
        {
            return false;
        }

        var year = ParseYear(movie.ReleaseDate);
        if (query.YearFrom is { } from && (year is null || year < from)) return false;
        return query.YearTo is not { } to || (year is not null && year <= to);
    }

    private static List<ListedMovie> SortWithinPage(IEnumerable<ListedMovie> movies, BrowseSort sort) => sort switch
    {
        BrowseSort.Rating => movies.OrderByDescending(m => m.VoteAverage ?? 0).ToList(),
        BrowseSort.Popularity => movies.OrderByDescending(m => m.Popularity ?? 0).ToList(),
        _ => movies.OrderByDescending(m => m.ReleaseDate ?? "", StringComparer.Ordinal).ToList()
    };

    /// <summary>Фильмы без IMDb ID пропускаются: на странице фильмов это ключ карточки.</summary>
    private async Task<IReadOnlyList<MovieTitle>> ResolveImdbIdsAsync(IReadOnlyList<ListedMovie> movies, CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(ExternalIdsConcurrency);
        var imdbIds = await Task.WhenAll(movies.Select(async movie =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                return await GetImdbIdAsync(movie.Id, ct);
            }
            finally
            {
                throttle.Release();
            }
        }));

        return movies
            .Zip(imdbIds, (movie, imdbId) => (Movie: movie, ImdbId: imdbId))
            .Where(x => ImdbId.IsValid(x.ImdbId))
            .DistinctBy(x => x.ImdbId)
            .Select(x => ToTitle(x.ImdbId!, x.Movie))
            .ToList();
    }

    private async Task<string?> GetImdbIdAsync(int tmdbId, CancellationToken ct)
    {
        var key = "tmdb:imdb:" + tmdbId.ToString(CultureInfo.InvariantCulture);
        if (memory.TryGetValue(key, out string? cached)) return cached;

        var ids = await GetAsync<ExternalIds>($"/3/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/external_ids", ct);
        var imdbId = string.IsNullOrEmpty(ids?.ImdbId) ? null : ids.ImdbId;
        memory.Set(key, imdbId, ExternalIdsTtl);
        return imdbId;
    }

    private MovieTitle ToTitle(string imdbId, ListedMovie movie) => new()
    {
        ImdbId = imdbId,
        Name = movie.Title ?? movie.OriginalTitle ?? imdbId,
        OriginalName = movie.OriginalTitle,
        Year = ParseYear(movie.ReleaseDate),
        Description = string.IsNullOrWhiteSpace(movie.Overview) ? null : movie.Overview,
        PosterUrl = string.IsNullOrEmpty(movie.PosterPath) ? null : Settings.ImagesUrl.TrimEnd('/') + movie.PosterPath,
        Ratings = TmdbRating(movie.VoteAverage, movie.VoteCount)
    };

    private void RequireToken()
    {
        if (string.IsNullOrEmpty(Settings.Token))
        {
            throw new CatalogUnavailableException("Для выбора фильмов по жанрам задайте токен TMDB в переменной TMDB_TOKEN");
        }
    }

    private static int? ParseYear(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var parsed) ? parsed.Year : null;

    /// <summary>TMDB на русском пишет жанры со строчной буквы: «мелодрама».</summary>
    private static string Capitalize(string name) =>
        name.Length == 0 ? name : char.ToUpper(name[0], RussianCulture) + name[1..];

    public async Task<MovieTitle?> GetAsync(string imdbId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(Settings.Token)) return null;

        var key = "tmdb:" + imdbId;
        if (memory.TryGetValue(key, out MovieTitle? cached) && cached is not null) return cached;

        var found = await GetAsync<FindResponse>($"/3/find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id", ct);
        var match = found?.MovieResults?.FirstOrDefault();
        if (match is null) return null;

        var details = await GetAsync<Details>($"/3/movie/{match.Id}", ct);
        if (details is null) return null;

        var title = ToTitle(imdbId, details);
        memory.Set(key, title, CacheTtl);
        return title;
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

    private MovieTitle ToTitle(string imdbId, Details details) => new()
    {
        ImdbId = imdbId,
        Name = details.Title ?? details.OriginalTitle ?? imdbId,
        OriginalName = details.OriginalTitle,
        Year = DateOnly.TryParse(details.ReleaseDate, CultureInfo.InvariantCulture, out var date) ? date.Year : null,
        RuntimeMinutes = details.Runtime is > 0 ? details.Runtime : null,
        Description = string.IsNullOrWhiteSpace(details.Overview) ? null : details.Overview,
        PosterUrl = string.IsNullOrEmpty(details.PosterPath) ? null : Settings.ImagesUrl.TrimEnd('/') + details.PosterPath,
        Genres = details.Genres?.Select(g => g.Name).OfType<string>().ToList() ?? [],
        Ratings = TmdbRating(details.VoteAverage, details.VoteCount)
    };

    private static IReadOnlyList<Rating> TmdbRating(double? average, int? votes) =>
        votes >= MinVotesToShow && Rating.Create(Rating.Tmdb, average) is { } rating ? [rating] : [];

    private sealed record FindResponse
    {
        public List<FindMatch>? MovieResults { get; init; }
    }

    private sealed record FindMatch(int Id);

    private sealed record Details
    {
        public string? Title { get; init; }
        public string? OriginalTitle { get; init; }
        public string? Overview { get; init; }
        public string? PosterPath { get; init; }
        public string? ReleaseDate { get; init; }
        public int? Runtime { get; init; }
        public List<GenreName>? Genres { get; init; }
        public double? VoteAverage { get; init; }
        public int? VoteCount { get; init; }
    }

    private sealed record GenreName(string? Name);

    private sealed record GenreList(List<GenreItem>? Genres);

    private sealed record GenreItem(int Id, string? Name);

    private sealed record MovieList(List<ListedMovie>? Results, int TotalPages);

    /// <summary>Запись /discover и /search: метаданные без IMDb ID.</summary>
    private sealed record ListedMovie
    {
        public int Id { get; init; }
        public string? Title { get; init; }
        public string? OriginalTitle { get; init; }
        public string? ReleaseDate { get; init; }
        public string? PosterPath { get; init; }
        public string? Overview { get; init; }
        public List<int>? GenreIds { get; init; }
        public double? Popularity { get; init; }
        public double? VoteAverage { get; init; }
        public int? VoteCount { get; init; }
    }

    private sealed record ExternalIds(string? ImdbId);
}
