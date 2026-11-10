using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Infrastructure.Catalog;

public sealed partial class ShikimoriCatalog(HttpClient http, SourcesConfigStore config, TimeProvider time) : ICatalog, IAnimeBrowser
{
    private const int SearchLimit = 20;

    /// <summary>Размер страницы выборки по жанрам; Shikimori отдаёт до 50.</summary>
    public const int BrowsePageSize = 24;

    /// <summary>Нижняя граница диапазона лет, когда задан только год «по».</summary>
    private const int EarliestYear = 1917;

    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

    private string BaseUrl => config.Current.Catalog.Url.TrimEnd('/');

    public async Task<IReadOnlyList<CatalogTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var path = $"/api/animes?search={Uri.EscapeDataString(query)}&limit={SearchLimit}";
        return await GetListAsync(path, ct);
    }

    public async Task<IReadOnlyList<CatalogTitle>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var path = $"/api/animes?ids={string.Join(',', ids)}&limit={ids.Count}";
        return await GetListAsync(path, ct);
    }

    public async Task<CatalogTitle?> GetAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{BaseUrl}/api/animes/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var anime = await response.Content.ReadFromJsonAsync<Anime>(SourceJson.Options, ct);
        return anime is null ? null : ToTitle(anime);
    }

    /// <summary>Жанры, темы и аудитории аниме: Shikimori фильтрует по ним одним параметром genre.</summary>
    public async Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken ct)
    {
        using var response = await http.GetAsync($"{BaseUrl}/api/genres", ct);
        response.EnsureSuccessStatusCode();
        var genres = await response.Content.ReadFromJsonAsync<List<GenreItem>>(SourceJson.Options, ct) ?? [];

        return genres
            .Where(g => g.EntryType == "Anime")
            .Select(g => new Genre(
                g.Id.ToString(CultureInfo.InvariantCulture),
                g.Russian ?? g.Name ?? g.Id.ToString(CultureInfo.InvariantCulture)))
            .OrderBy(g => g.Name, StringComparer.Create(RussianCulture, ignoreCase: true))
            .ToList();
    }

    public async Task<BrowsePage<CatalogTitle>> BrowseAsync(BrowseQuery query, CancellationToken ct)
    {
        var items = await GetListAsync(BuildBrowsePath(query), ct);
        return new BrowsePage<CatalogTitle>(items, query.Page, items.Count == BrowsePageSize);
    }

    internal string BuildBrowsePath(BrowseQuery query)
    {
        var parameters = new List<string>
        {
            $"limit={BrowsePageSize}",
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}",
            $"order={ToOrder(query.Sort)}"
        };

        // По дате выхода анонсы с датами на годы вперёд оказались бы первыми.
        if (query.Sort == BrowseSort.Date) parameters.Add("status=!anons");
        if (!string.IsNullOrEmpty(query.Genre)) parameters.Add($"genre={Uri.EscapeDataString(query.Genre)}");
        if (ToSeason(query.YearFrom, query.YearTo) is { } season) parameters.Add($"season={season}");
        if (!string.IsNullOrWhiteSpace(query.Text)) parameters.Add($"search={Uri.EscapeDataString(query.Text.Trim())}");

        return "/api/animes?" + string.Join('&', parameters);
    }

    private static string ToOrder(BrowseSort sort) => sort switch
    {
        BrowseSort.Rating => "ranked",
        BrowseSort.Popularity => "popularity",
        _ => "aired_on"
    };

    /// <summary>Годы выхода в формате Shikimori: «2019» или диапазон «2015_2020».</summary>
    private string? ToSeason(int? from, int? to)
    {
        if (from is null && to is null) return null;

        var start = from ?? EarliestYear;
        var end = to ?? time.GetUtcNow().Year + 1;
        return start == end
            ? start.ToString(CultureInfo.InvariantCulture)
            : $"{start.ToString(CultureInfo.InvariantCulture)}_{end.ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task<IReadOnlyList<CatalogTitle>> GetListAsync(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(BaseUrl + path, ct);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<Anime>>(SourceJson.Options, ct) ?? [];
        return items.Select(ToTitle).ToList();
    }

    private CatalogTitle ToTitle(Anime anime) => new()
    {
        Id = anime.Id,
        Name = string.IsNullOrWhiteSpace(anime.Russian) ? anime.Name ?? anime.Id.ToString(CultureInfo.InvariantCulture) : anime.Russian,
        OriginalName = anime.Name,
        JapaneseName = anime.Japanese?.FirstOrDefault(),
        Kind = anime.Kind,
        Year = ParseYear(anime.AiredOn),
        EpisodesTotal = anime.Episodes is > 0 ? anime.Episodes : anime.EpisodesAired,
        Status = anime.Status,
        Description = CleanDescription(anime.Description),
        PosterUrl = ToPosterUrl(anime.Image?.Original),
        Studio = anime.Studios?.FirstOrDefault()?.Name,
        Genres = anime.Genres?.Select(g => g.Russian).OfType<string>().ToList() ?? []
    };

    private string? ToPosterUrl(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains("missing_", StringComparison.Ordinal)) return null;
        return path.StartsWith("http", StringComparison.Ordinal) ? path : BaseUrl + path;
    }

    private static int? ParseYear(string? date) =>
        DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var parsed) ? parsed.Year : null;

    /// <summary>Описание приходит с BBCode вроде [character=1]Имя[/character].</summary>
    internal static string? CleanDescription(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : BbCode().Replace(text, "").Trim();

    [GeneratedRegex(@"\[/?[^\]]+\]")]
    private static partial Regex BbCode();

    private sealed record Anime
    {
        public int Id { get; init; }
        public string? Name { get; init; }
        public string? Russian { get; init; }
        public List<string?>? Japanese { get; init; }
        public string? Kind { get; init; }
        public string? Status { get; init; }
        public int? Episodes { get; init; }
        public int? EpisodesAired { get; init; }
        public string? AiredOn { get; init; }
        public string? Description { get; init; }
        public AnimeImage? Image { get; init; }
        public List<NamedRef>? Studios { get; init; }
        public List<GenreRef>? Genres { get; init; }
    }

    private sealed record AnimeImage(string? Original);

    private sealed record NamedRef(string? Name);

    private sealed record GenreRef(string? Russian);

    private sealed record GenreItem(int Id, string? Name, string? Russian, string? EntryType);
}
