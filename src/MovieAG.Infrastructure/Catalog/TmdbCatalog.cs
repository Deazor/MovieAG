using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Infrastructure.Catalog;

/// <summary>Метаданные фильмов из TMDB: IMDb ID переводится во внутренний ID через /find.</summary>
public sealed class TmdbCatalog(HttpClient http, SourcesConfigStore config, IMemoryCache memory) : IMovieCatalog
{
    private const string Language = "ru-RU";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);

    private MovieCatalogDefinition Settings => config.Current.Movies.Catalog;

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
        Genres = details.Genres?.Select(g => g.Name).OfType<string>().ToList() ?? []
    };

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
        public List<Genre>? Genres { get; init; }
    }

    private sealed record Genre(string? Name);
}
