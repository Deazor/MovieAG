using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Общий клиент Kodik API для аниме- и кино-источника.
/// Токен из конфига используется как есть. Без него берётся публичный из <see cref="IKodikTokenProvider"/>:
/// он принимается только на /get-player и находит только аниме.
/// </summary>
internal sealed class KodikApiClient(HttpClient http, SourceDefinition definition, IKodikTokenProvider? publicTokens)
{
    public const string DefaultUrl = "https://kodik-api.com";

    /// <summary>Любой существующий тайтл Shikimori: проверка связи лишь убеждается, что токен принят.</summary>
    private const int PingShikimoriId = 1;

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    public bool UsesPublicToken => string.IsNullOrEmpty(definition.Token);

    /// <summary>Запросы полного API (/search, /list); с публичным токеном Kodik их отвергает.</summary>
    public Task<KodikResponse> PostAsync(string path, CancellationToken ct, params (string Key, string Value)[] parameters) =>
        RequestAsync<KodikResponse>(HttpMethod.Post, path, ct, parameters);

    /// <summary>Ссылка на плеер по одному идентификатору (shikimoriID, imdbID…). Null, если плеера нет или он закрыт.</summary>
    public async Task<KodikPlayerResponse?> GetPlayerAsync(string idParameter, string id, CancellationToken ct)
    {
        var player = await RequestAsync<KodikPlayerResponse>(HttpMethod.Get, "/get-player", ct, (idParameter, id));
        return player is { Found: true, Allowed: 1, Link.Length: > 0 } ? player : null;
    }

    public Task PingAsync(string fullApiTypes, CancellationToken ct) => UsesPublicToken
        ? GetPlayerAsync("shikimoriID", PingShikimoriId.ToString(CultureInfo.InvariantCulture), ct)
        : PostAsync("/list", ct, ("limit", "1"), ("types", fullApiTypes));

    public static string NormalizeLink(string link) => link.StartsWith("//", StringComparison.Ordinal) ? "https:" + link : link;

    /// <summary>Ссылки Kodik на фильмы имеют вид //kodik…/video/…, на сериалы — //kodik…/serial/….</summary>
    public static bool IsMovieLink(string link) => link.Contains("/video/", StringComparison.Ordinal);

    private async Task<T> RequestAsync<T>(HttpMethod method, string path, CancellationToken ct, params (string Key, string Value)[] parameters)
        where T : KodikReply
    {
        if (!UsesPublicToken)
        {
            return (await SendAsync<T>(method, definition.Token!, path, parameters, ct)).GetResponse();
        }

        if (publicTokens is null)
        {
            throw new InvalidOperationException($"Для источника {definition.Id} не задан токен Kodik");
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

    /// <summary>На неверный токен Kodik отвечает статусом 401/403 и полем error с упоминанием токена.</summary>
    private static bool IsTokenRejected(HttpStatusCode status, string? error) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
        || (error is not null
            && (error.Contains("токен", StringComparison.OrdinalIgnoreCase)
                || error.Contains("token", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Итог одного запроса: ответ либо ошибка, и отдельно — отверг ли Kodik токен.</summary>
    private sealed record KodikAttempt<T>(T? Response, HttpRequestException? Error, bool TokenRejected)
        where T : KodikReply
    {
        public T GetResponse() =>
            Error is not null ? throw Error : Response ?? throw new InvalidOperationException("Пустой ответ Kodik");
    }
}

/// <summary>Любой ответ Kodik может вместо данных содержать поле error.</summary>
internal abstract record KodikReply
{
    public string? Error { get; init; }
}

internal sealed record KodikResponse(List<KodikResult>? Results) : KodikReply;

internal sealed record KodikPlayerResponse(bool Found, int? Allowed, string? Translation, string? Link) : KodikReply;

/// <summary>Запись /search и /list: один материал в одной озвучке.</summary>
internal sealed record KodikResult
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
    public string? ImdbId { get; init; }
    public KodikTranslation? Translation { get; init; }
    public Dictionary<string, KodikSeason>? Seasons { get; init; }
    public KodikMaterialData? MaterialData { get; init; }
}

internal sealed record KodikTranslation(int Id, string? Title, string? Type);

internal sealed record KodikSeason(string? Link, Dictionary<string, JsonElement>? Episodes);

/// <summary>Метаданные with_material_data; у фильмов Kodik берёт их с Кинопоиска.</summary>
internal sealed record KodikMaterialData(
    string? AnimePosterUrl,
    string? PosterUrl,
    string? Description,
    List<string>? Genres,
    int? Duration);
