using System.Globalization;
using System.Net;
using System.Text.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// Фильмы из балансера Alloha (api.alloha.tv), воспроизведение через iframe. Нужен собственный токен из кабинета Alloha.
/// Ищет по IMDb ID и по названию, у фильма отдельный вариант просмотра на каждую озвучку.
/// Списка новинок в API нет, сериалы на странице фильмов пропускаются.
/// </summary>
public sealed class AllohaMovieSource(HttpClient http, MovieSourceDefinition definition) : IMovieSource
{
    public const string AdapterName = "alloha";
    public const string DefaultStreamId = "default";
    private const string DefaultUrl = "https://api.alloha.tv";

    /// <summary>«Зелёная миля»: проверка связи лишь убеждается, что Alloha принимает токен.</summary>
    private const string PingImdbId = "tt0120689";

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    public string Id => definition.Id;

    public async Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var items = ImdbId.IsValid(query)
            ? await RequestAsync(("imdb", query), ct)
            : await RequestAsync(("name", query), ct);

        return items
            .Where(IsMovie)
            .GroupBy(i => i.IdImdb!, StringComparer.Ordinal)
            .Select(g => ToTitle(g.First()))
            .ToList();
    }

    public Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MovieTitle>>([]);

    public async Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct)
    {
        var item = await FindMovieAsync(imdbId, ct);
        if (item is null) return null;

        var streams = ListStreams(item);
        return streams.Count == 0 ? null : new SourceMovie(ToTitle(item), streams);
    }

    public async Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct)
    {
        var item = await FindMovieAsync(imdbId, ct);
        if (item is null) return null;

        var link = streamId == DefaultStreamId
            ? item.Iframe
            : item.TranslationIframe?.GetValueOrDefault(streamId)?.Iframe;
        return string.IsNullOrEmpty(link) ? null : new Playback(PlaybackKind.Iframe, NormalizeLink(link), []);
    }

    public Task PingAsync(CancellationToken ct) => RequestAsync(("imdb", PingImdbId), ct);

    private async Task<AllohaItem?> FindMovieAsync(string imdbId, CancellationToken ct)
    {
        if (!ImdbId.IsValid(imdbId)) return null;

        var items = await RequestAsync(("imdb", imdbId), ct);
        return items.FirstOrDefault(i => IsMovie(i) && i.IdImdb == imdbId);
    }

    /// <summary>
    /// Alloha всегда отвечает 200 и text/html, а ошибку передаёт в status/error_info.
    /// «Не найдено» превращается в пустой список, отказ в токене — в исключение.
    /// </summary>
    private async Task<IReadOnlyList<AllohaItem>> RequestAsync((string Key, string Value) filter, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(definition.Token))
        {
            throw new InvalidOperationException($"Для источника {Id} не задан токен Alloha");
        }

        var url = $"{_baseUrl}/?token={Uri.EscapeDataString(definition.Token)}&{filter.Key}={Uri.EscapeDataString(filter.Value)}";
        using var response = await http.GetAsync(url, ct);

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

    /// <summary>У сериалов есть seasons; без IMDb ID карточку фильма не построить.</summary>
    private static bool IsMovie(AllohaItem item) =>
        item.Seasons is not { ValueKind: JsonValueKind.Object } && ImdbId.IsValid(item.IdImdb);

    private List<MovieStream> ListStreams(AllohaItem item)
    {
        var streams = (item.TranslationIframe ?? [])
            .Where(t => !string.IsNullOrEmpty(t.Value.Iframe))
            .Select(t => new MovieStream(Id, t.Key, Label(t.Value)))
            .ToList();

        // Если озвучки не перечислены, остаётся общий плеер фильма.
        if (streams.Count == 0 && !string.IsNullOrEmpty(item.Iframe))
        {
            streams.Add(new MovieStream(Id, DefaultStreamId, "Alloha"));
        }

        return streams;
    }

    private static string Label(AllohaTranslation translation)
    {
        var name = translation.Name ?? "Без названия";
        return string.IsNullOrEmpty(translation.Quality) ? name : $"{name} · {translation.Quality}";
    }

    private static MovieTitle ToTitle(AllohaItem item) => new()
    {
        ImdbId = item.IdImdb!,
        Name = item.Name ?? item.OriginalName ?? item.IdImdb!,
        OriginalName = item.OriginalName,
        Year = item.Year,
        RuntimeMinutes = ParseRuntime(item.Time),
        Description = item.Description,
        PosterUrl = item.Poster,
        Genres = (item.Genre ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList()
    };

    /// <summary>Длительность приходит строкой вида «03:09» (часы:минуты).</summary>
    private static int? ParseRuntime(string? time) =>
        TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var span) && span > TimeSpan.Zero
            ? (int)span.TotalMinutes
            : null;

    private static string NormalizeLink(string link) => link.StartsWith("//", StringComparison.Ordinal) ? "https:" + link : link;

    private sealed record AllohaResponse(string? Status, string? ErrorInfo, JsonElement? Data);

    private sealed record AllohaItem
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
        public JsonElement? Seasons { get; init; }
    }

    private sealed record AllohaTranslation(string? Name, string? Iframe, string? Quality);
}
