using System.Globalization;
using System.Text.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Балансер Kodik, воспроизведение через iframe.
/// С собственным токеном доступен весь API: поиск, новинки, одна запись на озвучку.
/// Без токена берётся публичный из <see cref="IKodikTokenProvider"/>, а он открывает только /get-player:
/// тайтлы приходят из каталога, Kodik даёт на них один плеер, а озвучка выбирается внутри плеера.
/// </summary>
public sealed class KodikSource(HttpClient http, SourceDefinition definition, IKodikTokenProvider? publicTokens = null) : IAnimeSource
{
    public const string AdapterName = "kodik";
    public const string PlayerDubId = "player";
    private const string AnimeTypes = "anime,anime-serial";
    private const int MaxResults = 100;

    private readonly KodikApiClient _api = new(http, definition, publicTokens);

    public string Id => definition.Id;

    private bool UsesPublicToken => _api.UsesPublicToken;

    public async Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct)
    {
        // Публичный токен не даёт /search: тайтл найдётся через каталог, плеер — в GetVariantsAsync.
        if (UsesPublicToken) return [];

        var response = await _api.PostAsync("/search", ct,
            ("title", query),
            ("types", AnimeTypes),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToSourceTitles(response.Results);
    }

    public async Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        if (UsesPublicToken) return [];

        var response = await _api.PostAsync("/list", ct,
            ("types", AnimeTypes),
            ("sort", "updated_at"),
            ("order", "desc"),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToSourceTitles(response.Results).Take(limit).ToList();
    }

    public async Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct)
    {
        if (UsesPublicToken)
        {
            var player = await GetPlayerAsync(title.Id, ct);
            return player is null
                ? []
                : [new DubVariant(Id, PlayerDubId, $"{player.Translation ?? "Kodik"} и другие (выбор в плеере)", DubKind.Voice, ListPlayerEpisodes(title, player.Link!))];
        }

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
        if (UsesPublicToken)
        {
            if (dubId != PlayerDubId) return null;
            var player = await GetPlayerAsync(title.Id, ct);
            return player is null ? null : new Playback(PlaybackKind.Iframe, WithEpisode(KodikApiClient.NormalizeLink(player.Link!), episode), []);
        }

        var results = await GetByShikimoriIdAsync(title.Id, ct);
        var result = results.FirstOrDefault(r => r.Translation?.Id.ToString(CultureInfo.InvariantCulture) == dubId);
        if (result is null) return null;

        var link = FindEpisodeLink(result, episode);
        return link is null ? null : new Playback(PlaybackKind.Iframe, KodikApiClient.NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => _api.PingAsync(AnimeTypes, ct);

    private Task<KodikPlayerResponse?> GetPlayerAsync(int shikimoriId, CancellationToken ct) =>
        _api.GetPlayerAsync("shikimoriID", shikimoriId.ToString(CultureInfo.InvariantCulture), ct);

    /// <summary>
    /// /get-player не перечисляет серии, поэтому их число берётся из каталога.
    /// Остальные серии и сезоны всё равно доступны в селекторе плеера.
    /// </summary>
    private static IReadOnlyList<int> ListPlayerEpisodes(CatalogTitle title, string link)
    {
        if (KodikApiClient.IsMovieLink(link)) return [1];
        var count = title.EpisodesTotal is > 0 ? title.EpisodesTotal.Value : 1;
        return Enumerable.Range(1, count).ToList();
    }

    private static string WithEpisode(string link, int episode) => KodikApiClient.IsMovieLink(link)
        ? link
        : $"{link}{(link.Contains('?') ? '&' : '?')}episode={episode.ToString(CultureInfo.InvariantCulture)}";

    private async Task<List<KodikResult>> GetByShikimoriIdAsync(int shikimoriId, CancellationToken ct)
    {
        var response = await _api.PostAsync("/search", ct,
            ("shikimori_id", shikimoriId.ToString(CultureInfo.InvariantCulture)),
            ("with_episodes", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return (response.Results ?? [])
            .Where(r => SourceJson.ParseIntFlexible(r.ShikimoriId) == shikimoriId)
            .ToList();
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
}
