using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>Балансер Kodik: одна запись на озвучку, воспроизведение через iframe.</summary>
public sealed class KodikSource(HttpClient http, SourceDefinition definition) : IAnimeSource
{
    public const string AdapterName = "kodik";
    private const string DefaultUrl = "https://kodik-api.com";
    private const string AnimeTypes = "anime,anime-serial";
    private const int MaxResults = 100;

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    public string Id => definition.Id;

    public async Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var response = await PostAsync("/search", ct,
            ("title", query),
            ("types", AnimeTypes),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToSourceTitles(response.Results);
    }

    public async Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        var response = await PostAsync("/list", ct,
            ("types", AnimeTypes),
            ("sort", "updated_at"),
            ("order", "desc"),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToSourceTitles(response.Results).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct)
    {
        var results = await GetByShikimoriIdAsync(title.Id, ct);
        return results
            .Where(r => r.Translation is not null)
            .GroupBy(r => r.Translation!.Id)
            .Select(g => ToVariant(g.First()))
            .Where(v => v.Episodes.Count > 0)
            .ToList();
    }

    public async Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct)
    {
        var results = await GetByShikimoriIdAsync(title.Id, ct);
        var result = results.FirstOrDefault(r => r.Translation?.Id.ToString(CultureInfo.InvariantCulture) == dubId);
        if (result is null) return null;

        var link = FindEpisodeLink(result, episode);
        return link is null ? null : new Playback(PlaybackKind.Iframe, NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => PostAsync("/list", ct, ("limit", "1"), ("types", AnimeTypes));

    private async Task<List<KodikResult>> GetByShikimoriIdAsync(int shikimoriId, CancellationToken ct)
    {
        var response = await PostAsync("/search", ct,
            ("shikimori_id", shikimoriId.ToString(CultureInfo.InvariantCulture)),
            ("with_episodes", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return (response.Results ?? [])
            .Where(r => SourceJson.ParseIntFlexible(r.ShikimoriId) == shikimoriId)
            .ToList();
    }

    private async Task<KodikResponse> PostAsync(string path, CancellationToken ct, params (string Key, string Value)[] parameters)
    {
        var query = string.Join('&', parameters
            .Prepend(("token", definition.Token ?? ""))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));

        using var response = await http.PostAsync($"{_baseUrl}{path}?{query}", content: null, ct);

        // В сообщение об ошибке не попадает URL, поэтому токен не утечёт в лог.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Kodik вернул {(int)response.StatusCode}", null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<KodikResponse>(SourceJson.Options, ct)
            ?? throw new InvalidOperationException("Пустой ответ Kodik");
    }

    private IReadOnlyList<SourceTitle> ToSourceTitles(List<KodikResult>? results)
    {
        return (results ?? [])
            .Select(r => (Result: r, ShikimoriId: SourceJson.ParseIntFlexible(r.ShikimoriId)))
            .Where(x => x.ShikimoriId is not null)
            .GroupBy(x => x.ShikimoriId!.Value)
            .Select(g =>
            {
                var first = g.First().Result;
                return new SourceTitle
                {
                    ShikimoriId = g.Key,
                    SourceId = Id,
                    Name = first.Title ?? first.TitleOrig ?? g.Key.ToString(CultureInfo.InvariantCulture),
                    OriginalName = first.TitleOrig,
                    Kind = SourceJson.NormalizeKind(first.Type),
                    Year = first.Year,
                    EpisodesTotal = first.EpisodesCount,
                    PosterUrl = first.MaterialData?.AnimePosterUrl ?? first.MaterialData?.PosterUrl,
                    DubNames = g
                        .Select(x => x.Result.Translation?.Title)
                        .OfType<string>()
                        .Distinct()
                        .ToList(),
                    LatestEpisode = first.LastEpisode
                };
            })
            .ToList();
    }

    private DubVariant ToVariant(KodikResult result)
    {
        var translation = result.Translation!;
        return new DubVariant(
            Id,
            translation.Id.ToString(CultureInfo.InvariantCulture),
            translation.Title ?? "Без названия",
            translation.Type == "subtitles" ? DubKind.Subtitles : DubKind.Voice,
            ListEpisodes(result));
    }

    /// <summary>У фильма нет сезонов: это одна «серия» со ссылкой на запись.</summary>
    private static IReadOnlyList<int> ListEpisodes(KodikResult result)
    {
        var season = PickSeason(result);
        if (season?.Episodes is null)
        {
            return string.IsNullOrEmpty(result.Link) ? [] : [1];
        }

        return season.Episodes.Keys
            .Select(k => int.TryParse(k, out var n) ? n : (int?)null)
            .OfType<int>()
            .Order()
            .ToList();
    }

    private static string? FindEpisodeLink(KodikResult result, int episode)
    {
        var season = PickSeason(result);
        if (season?.Episodes is null)
        {
            return episode == 1 ? result.Link : null;
        }

        return season.Episodes.TryGetValue(episode.ToString(CultureInfo.InvariantCulture), out var value)
            ? ReadLink(value)
            : null;
    }

    /// <summary>Записи Kodik обычно относятся к одному сезону Shikimori; сезон 0 — это спецвыпуски.</summary>
    private static KodikSeason? PickSeason(KodikResult result)
    {
        if (result.Seasons is null || result.Seasons.Count == 0) return null;

        var key = result.LastSeason?.ToString(CultureInfo.InvariantCulture);
        if (key is not null && result.Seasons.TryGetValue(key, out var last)) return last;

        return result.Seasons
            .OrderByDescending(s => s.Key != "0")
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .First().Value;
    }

    private static string? ReadLink(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Object when value.TryGetProperty("link", out var link) => link.GetString(),
        _ => null
    };

    private static string NormalizeLink(string link) => link.StartsWith("//", StringComparison.Ordinal) ? "https:" + link : link;

    private sealed record KodikResponse(List<KodikResult>? Results);

    private sealed record KodikResult
    {
        public string? Type { get; init; }
        public string? Link { get; init; }
        public string? Title { get; init; }
        public string? TitleOrig { get; init; }
        public int? Year { get; init; }
        public int? LastSeason { get; init; }
        public int? LastEpisode { get; init; }
        public int? EpisodesCount { get; init; }
        public JsonElement? ShikimoriId { get; init; }
        public KodikTranslation? Translation { get; init; }
        public Dictionary<string, KodikSeason>? Seasons { get; init; }
        public KodikMaterialData? MaterialData { get; init; }
    }

    private sealed record KodikTranslation(int Id, string? Title, string? Type);

    private sealed record KodikSeason(string? Link, Dictionary<string, JsonElement>? Episodes);

    private sealed record KodikMaterialData(string? AnimePosterUrl, string? PosterUrl);
}
