using System.Globalization;
using System.Net;
using System.Net.Http.Json;
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
    private const string DefaultUrl = "https://kodik-api.com";
    private const string AnimeTypes = "anime,anime-serial";
    private const int MaxResults = 100;

    /// <summary>Любой существующий тайтл Shikimori: проверка связи лишь убеждается, что токен принят.</summary>
    private const int PingShikimoriId = 1;

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    public string Id => definition.Id;

    private bool UsesPublicToken => string.IsNullOrEmpty(definition.Token);

    public async Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct)
    {
        // Публичный токен не даёт /search: тайтл найдётся через каталог, плеер — в GetVariantsAsync.
        if (UsesPublicToken) return [];

        var response = await PostAsync("/search", ct,
            ("title", query),
            ("types", AnimeTypes),
            ("with_material_data", "true"),
            ("limit", MaxResults.ToString(CultureInfo.InvariantCulture)));
        return ToSourceTitles(response.Results);
    }

    public async Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        if (UsesPublicToken) return [];

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
            return player is null ? null : new Playback(PlaybackKind.Iframe, WithEpisode(NormalizeLink(player.Link!), episode), []);
        }

        var results = await GetByShikimoriIdAsync(title.Id, ct);
        var result = results.FirstOrDefault(r => r.Translation?.Id.ToString(CultureInfo.InvariantCulture) == dubId);
        if (result is null) return null;

        var link = FindEpisodeLink(result, episode);
        return link is null ? null : new Playback(PlaybackKind.Iframe, NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => UsesPublicToken
        ? GetPlayerAsync(PingShikimoriId, ct)
        : PostAsync("/list", ct, ("limit", "1"), ("types", AnimeTypes));

    /// <summary>Единственный запрос, который принимает публичный токен. Null, если плеера нет или он закрыт.</summary>
    private async Task<KodikPlayerResponse?> GetPlayerAsync(int shikimoriId, CancellationToken ct)
    {
        var player = await RequestAsync<KodikPlayerResponse>(HttpMethod.Get, "/get-player", ct,
            ("shikimoriID", shikimoriId.ToString(CultureInfo.InvariantCulture)));
        return player is { Found: true, Allowed: 1, Link.Length: > 0 } ? player : null;
    }

    /// <summary>
    /// /get-player не перечисляет серии, поэтому их число берётся из каталога.
    /// Остальные серии и сезоны всё равно доступны в селекторе плеера.
    /// </summary>
    private static IReadOnlyList<int> ListPlayerEpisodes(CatalogTitle title, string link)
    {
        if (IsMovieLink(link)) return [1];
        var count = title.EpisodesTotal is > 0 ? title.EpisodesTotal.Value : 1;
        return Enumerable.Range(1, count).ToList();
    }

    private static string WithEpisode(string link, int episode) => IsMovieLink(link)
        ? link
        : $"{link}{(link.Contains('?') ? '&' : '?')}episode={episode.ToString(CultureInfo.InvariantCulture)}";

    private static bool IsMovieLink(string link) => link.Contains("/video/", StringComparison.Ordinal);

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

    private Task<KodikResponse> PostAsync(string path, CancellationToken ct, params (string Key, string Value)[] parameters) =>
        RequestAsync<KodikResponse>(HttpMethod.Post, path, ct, parameters);

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, CancellationToken ct, params (string Key, string Value)[] parameters)
        where T : KodikReply
    {
        if (!UsesPublicToken)
        {
            return (await SendAsync<T>(method, definition.Token!, path, parameters, ct)).GetResponse();
        }

        if (publicTokens is null)
        {
            throw new InvalidOperationException($"Для источника {Id} не задан токен Kodik");
        }

        var token = await publicTokens.GetTokenAsync(ct);
        var attempt = await SendAsync<T>(method, token, path, parameters, ct);

        if (attempt.TokenRejected)
        {
            // Kodik время от времени меняет публичный токен: берём свежий из скрипта и повторяем один раз.
            publicTokens.Invalidate(token);
            attempt = await SendAsync<T>(method, await publicTokens.GetTokenAsync(ct), path, parameters, ct);
        }

        return attempt.GetResponse();
    }

    private async Task<KodikAttempt<T>> SendAsync<T>(
        HttpMethod method, string token, string path, (string Key, string Value)[] parameters, CancellationToken ct)
        where T : KodikReply
    {
        var query = string.Join('&', parameters
            .Prepend(("token", token))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));

        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}?{query}");
        using var response = await http.SendAsync(request, ct);
        var body = await ReadResponseAsync<T>(response, ct);

        if (response.IsSuccessStatusCode && body?.Error is null)
        {
            return new KodikAttempt<T>(body, null, false);
        }

        // В сообщение об ошибке не попадает URL, поэтому токен не утечёт в лог.
        var message = $"Kodik вернул {(int)response.StatusCode}" + (body?.Error is { } error ? $": {error}" : "");
        return new KodikAttempt<T>(
            null,
            new HttpRequestException(message, null, response.StatusCode),
            IsTokenRejected(response.StatusCode, body?.Error));
    }

    /// <summary>Тело ошибки бывает не JSON (например, страница прокси), тогда оно игнорируется.</summary>
    private static async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
        where T : KodikReply
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(SourceJson.Options, ct);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            return null;
        }
    }

    /// <summary>На неверный токен Kodik отвечает либо статусом 401/403, либо полем error с упоминанием токена.</summary>
    private static bool IsTokenRejected(HttpStatusCode status, string? error) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        || (error is not null
            && (error.Contains("токен", StringComparison.OrdinalIgnoreCase)
                || error.Contains("token", StringComparison.OrdinalIgnoreCase)));

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

    /// <summary>Любой ответ Kodik может вместо данных содержать поле error.</summary>
    private abstract record KodikReply
    {
        public string? Error { get; init; }
    }

    private sealed record KodikResponse(List<KodikResult>? Results) : KodikReply;

    private sealed record KodikPlayerResponse(bool Found, int? Allowed, string? Translation, string? Link) : KodikReply;

    /// <summary>Итог одного запроса: ответ либо ошибка, и отдельно — отверг ли Kodik токен.</summary>
    private sealed record KodikAttempt<T>(T? Response, HttpRequestException? Error, bool TokenRejected)
        where T : KodikReply
    {
        public T GetResponse() =>
            Error is not null ? throw Error : Response ?? throw new InvalidOperationException("Пустой ответ Kodik");
    }

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
