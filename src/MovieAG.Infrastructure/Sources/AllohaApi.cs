using System.Globalization;
using System.Net;
using System.Text.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>Общий клиент API Alloha (api.alloha.tv) для источников фильмов и сериалов. Нужен собственный токен.</summary>
internal sealed class AllohaApiClient(HttpClient http, SourceDefinition definition)
{
    public const string DefaultUrl = "https://api.alloha.tv";

    /// <summary>Верхняя граница страниц за один показ новинок, чтобы уложиться в таймаут источника.</summary>
    private const int MaxLatestPages = 3;

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    /// <summary>Поиск по IMDb ID или по части названия: без list=1 Alloha ищет одну запись с точным совпадением.</summary>
    public Task<IReadOnlyList<AllohaItem>> SearchAsync(string query, CancellationToken ct) =>
        ImdbId.IsValid(query)
            ? RequestAsync(ct, ("imdb", query))
            : RequestAsync(ct, ("name", query), ("list", "1"));

    public Task<IReadOnlyList<AllohaItem>> FindByImdbAsync(string imdbId, CancellationToken ct) =>
        RequestAsync(ct, ("imdb", imdbId));

    /// <summary>
    /// Новинки списка movie или serial. Alloha сортирует по дате добавления к себе, поэтому подходящие
    /// записи отбирает <paramref name="accept"/>, а страницы листаются, пока их не наберётся <paramref name="count"/>.
    /// </summary>
    public async Task<List<AllohaItem>> GetLatestAsync(string list, int count, Func<AllohaItem, bool> accept, CancellationToken ct)
    {
        var found = new List<AllohaItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; page <= MaxLatestPages && found.Count < count; page++)
        {
            var items = await RequestAsync(ct,
                ("list", list),
                ("order", "date"),
                ("page", page.ToString(CultureInfo.InvariantCulture)));
            if (items.Count == 0) break;

            found.AddRange(items.Where(i => accept(i) && seen.Add(i.IdImdb!)));
        }

        return found.Take(count).ToList();
    }

    /// <summary>
    /// Alloha всегда отвечает 200 и text/html, а ошибку передаёт в status/error_info.
    /// «Не найдено» превращается в пустой список, отказ в токене — в исключение.
    /// </summary>
    public async Task<IReadOnlyList<AllohaItem>> RequestAsync(CancellationToken ct, params (string Key, string Value)[] parameters)
    {
        if (string.IsNullOrEmpty(definition.Token))
        {
            throw new InvalidOperationException($"Для источника {definition.Id} не задан токен Alloha");
        }

        var query = string.Join('&', parameters
            .Prepend(("token", definition.Token))
            .Select(p => $"{p.Item1}={Uri.EscapeDataString(p.Item2)}"));
        using var response = await http.GetAsync($"{_baseUrl}/?{query}", ct);

        // В сообщения об ошибках не попадает URL, поэтому токен не утечёт в лог.
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Alloha вернул {(int)response.StatusCode}", null, response.StatusCode);
        }

        var body = JsonSerializer.Deserialize<AllohaResponse>(await response.Content.ReadAsStringAsync(ct), SourceJson.Options)
            ?? throw new InvalidOperationException("Пустой ответ Alloha");

        if (body.Status != "success")
        {
            var error = body.ErrorInfo ?? "неизвестная ошибка";
            if (error.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                throw new HttpRequestException($"Alloha отклонил токен: {error}", null, HttpStatusCode.Unauthorized);
            }

            return [];
        }

        return body.Data?.ValueKind switch
        {
            JsonValueKind.Object => [body.Data.Value.Deserialize<AllohaItem>(SourceJson.Options)!],
            JsonValueKind.Array => body.Data.Value.Deserialize<List<AllohaItem>>(SourceJson.Options) ?? [],
            _ => []
        };
    }

    /// <summary>У сериалов в ответе есть объект seasons; без IMDb ID карточку не построить.</summary>
    public static bool IsSeries(AllohaItem item) => item.Seasons is { ValueKind: JsonValueKind.Object };

    public static string Label(string? name, string? quality)
    {
        var label = name ?? "Без названия";
        return string.IsNullOrEmpty(quality) ? label : $"{label} · {quality}";
    }

    public static IReadOnlyList<string> Genres(AllohaItem item) =>
        (item.Genre ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static IReadOnlyList<Rating> Ratings(AllohaItem item) =>
        new[] { Rating.Create(Rating.Imdb, item.RatingImdb), Rating.Create(Rating.Kinopoisk, item.RatingKp) }
            .OfType<Rating>()
            .ToList();

    public static string NormalizeLink(string link) => link.StartsWith("//", StringComparison.Ordinal) ? "https:" + link : link;

    private sealed record AllohaResponse(string? Status, string? ErrorInfo, JsonElement? Data);
}

/// <summary>Фильм или сериал в ответе Alloha; формат сверен с фикстурами открытого клиента anime-dl-core.</summary>
internal sealed record AllohaItem
{
    public string? Name { get; init; }
    public string? OriginalName { get; init; }
    public int? Year { get; init; }
    public string? IdImdb { get; init; }
    public string? Genre { get; init; }
    public string? Time { get; init; }
    public string? Poster { get; init; }
    public string? Description { get; init; }
    public string? Iframe { get; init; }
    public Dictionary<string, AllohaTranslation>? TranslationIframe { get; init; }

    /// <summary>Сезоны сериала: { "1": { season, episodes: { "1": { episode, translation: { "10": {...} } } } } }.</summary>
    public JsonElement? Seasons { get; init; }

    public int? SeasonsCount { get; init; }

    public double? RatingImdb { get; init; }
    public double? RatingKp { get; init; }
}

/// <summary>Озвучка: на уровне фильма поле называется name, на уровне серии — translation.</summary>
internal sealed record AllohaTranslation(string? Name, string? Translation, string? Iframe, string? Quality)
{
    public string? DisplayName => Name ?? Translation;
}
