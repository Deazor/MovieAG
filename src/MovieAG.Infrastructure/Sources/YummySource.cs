using System.Globalization;
using System.Net.Http.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>
/// API Yummy Anime: по каждой серии отдаёт iframe разных плееров с разными озвучками.
/// Токен необязателен: client_id из token отправляется, только если он задан.
/// </summary>
public sealed class YummySource(HttpClient http, SourceDefinition definition) : IAnimeSource
{
    public const string AdapterName = "yummy";
    private const string DefaultUrl = "https://api.yani.tv";
    private const string ApplicationHeader = "X-Application";
    private const string SubtitlesPrefix = "Субтитры";
    private const string VoicePrefix = "Озвучка ";
    private const string PlayerPrefix = "Плеер ";
    private const char DubIdSeparator = '|';
    private const int SearchLimit = 20;

    /// <summary>Более короткие запросы API отклоняет с кодом 400.</summary>
    private const int MinQueryLength = 3;

    /// <summary>Список серий тяжёлый, а на просмотр одного тайтла запрашивается многократно.</summary>
    private static readonly TimeSpan VideosTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Порядок плееров внутри одной озвучки; остальные идут после них.
    /// Kodik и CVH проверены вживую, Aksor не подтверждён, Alloha вне сайта Yummy показывал ошибку.
    /// </summary>
    private static readonly string[] PlayerPriority = ["Kodik", "CVH", "Aksor", "Alloha"];

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');
    private volatile VideosCacheEntry? _videosCache;

    public string Id => definition.Id;

    public async Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct)
    {
        if (query.Trim().Length < MinQueryLength) return [];

        var found = await SearchAnimeAsync(query, ct);
        return found.Where(a => ShikimoriIdOf(a) is not null).Select(ToSourceTitle).ToList();
    }

    /// <summary>В ленте /feed нет ID Shikimori, поэтому склеить её с каталогом нельзя.</summary>
    public Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SourceTitle>>([]);

    public async Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct)
    {
        var animeId = await FindAnimeIdAsync(title, ct);
        if (animeId is null) return [];

        var videos = await GetVideosAsync(animeId.Value, ct);
        return videos
            .Where(v => v.Data?.Dubbing is not null && v.Data.PlayerId is not null)
            .GroupBy(v => v.Data!.Dubbing!)
            .SelectMany(dub => dub
                .GroupBy(v => v.Data!.PlayerId!.Value)
                .OrderBy(player => PlayerRank(PlayerLabel(player.First())))
                .Select(player => ToVariant(animeId.Value, dub.Key, player.Key, player.ToList())))
            .Where(v => v.Episodes.Count > 0)
            .ToList();
    }

    public async Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct)
    {
        var parts = dubId.Split(DubIdSeparator, 3);
        if (parts.Length != 3
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out var animeId)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out var playerId))
        {
            return null;
        }

        var videos = await GetVideosAsync(animeId, ct);
        var video = videos.FirstOrDefault(v =>
            v.Data?.PlayerId == playerId
            && v.Data.Dubbing == parts[2]
            && EpisodeNumber(v) == episode);

        return string.IsNullOrEmpty(video?.IframeUrl)
            ? null
            : new Playback(PlaybackKind.Iframe, AbsoluteUrl(video.IframeUrl), []);
    }

    public Task PingAsync(CancellationToken ct) => GetAsync<List<Anime>>("/anime?limit=1", ct);

    private async Task<int?> FindAnimeIdAsync(CatalogTitle title, CancellationToken ct)
    {
        var queries = new[] { title.OriginalName, title.Name }
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct();

        foreach (var query in queries)
        {
            var match = (await SearchAnimeAsync(query!, ct)).FirstOrDefault(a => ShikimoriIdOf(a) == title.Id);
            if (match is not null) return match.AnimeId;
        }

        return null;
    }

    private Task<List<Anime>> SearchAnimeAsync(string query, CancellationToken ct) =>
        GetAsync<List<Anime>>($"/search?q={Uri.EscapeDataString(query)}&limit={SearchLimit}", ct);

    private async Task<List<Video>> GetVideosAsync(int animeId, CancellationToken ct)
    {
        var cached = _videosCache;
        if (cached is not null && cached.AnimeId == animeId && DateTimeOffset.UtcNow - cached.LoadedAt < VideosTtl)
        {
            return cached.Videos;
        }

        var videos = await GetAsync<List<Video>>($"/anime/{animeId}/videos", ct);
        _videosCache = new VideosCacheEntry(animeId, DateTimeOffset.UtcNow, videos);
        return videos;
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        if (!string.IsNullOrEmpty(definition.Token))
        {
            request.Headers.TryAddWithoutValidation(ApplicationHeader, definition.Token);
        }

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(SourceJson.Options, ct);
        return envelope is { Response: not null }
            ? envelope.Response
            : throw new InvalidOperationException("Пустой ответ Yummy Anime");
    }

    private SourceTitle ToSourceTitle(Anime anime) => new()
    {
        ShikimoriId = ShikimoriIdOf(anime)!.Value,
        SourceId = Id,
        Name = anime.Title ?? anime.AnimeId.ToString(CultureInfo.InvariantCulture),
        Kind = SourceJson.NormalizeKind(anime.Type?.Alias),
        Year = anime.Year,
        PosterUrl = string.IsNullOrEmpty(anime.Poster?.Fullsize) ? null : AbsoluteUrl(anime.Poster.Fullsize)
    };

    private DubVariant ToVariant(int animeId, string dubbing, int playerId, IReadOnlyList<Video> videos)
    {
        var isSubtitles = dubbing.StartsWith(SubtitlesPrefix, StringComparison.Ordinal);
        var name = dubbing.StartsWith(VoicePrefix, StringComparison.Ordinal) ? dubbing[VoicePrefix.Length..] : dubbing;

        return new DubVariant(
            Id,
            string.Join(DubIdSeparator, animeId.ToString(CultureInfo.InvariantCulture), playerId.ToString(CultureInfo.InvariantCulture), dubbing),
            name,
            isSubtitles ? DubKind.Subtitles : DubKind.Voice,
            videos.Select(EpisodeNumber).OfType<int>().Distinct().Order().ToList(),
            PlayerLabel(videos[0]));
    }

    private static int? ShikimoriIdOf(Anime anime) => anime.RemoteIds?.ShikimoriId is > 0 and var id ? id : null;

    private static string PlayerLabel(Video video)
    {
        var player = video.Data?.Player ?? "";
        return player.StartsWith(PlayerPrefix, StringComparison.Ordinal) ? player[PlayerPrefix.Length..] : player;
    }

    private static int PlayerRank(string label)
    {
        var rank = Array.IndexOf(PlayerPriority, label);
        return rank < 0 ? PlayerPriority.Length : rank;
    }

    /// <summary>У фильмов номер может быть пустым, это единственная «серия». Спецвыпуски вроде 12.5 пропускаем.</summary>
    private static int? EpisodeNumber(Video video)
    {
        if (string.IsNullOrWhiteSpace(video.Number)) return 1;
        return int.TryParse(video.Number, CultureInfo.InvariantCulture, out var number) && number > 0 ? number : null;
    }

    private static string AbsoluteUrl(string url) => url.StartsWith("//", StringComparison.Ordinal) ? "https:" + url : url;

    private sealed record VideosCacheEntry(int AnimeId, DateTimeOffset LoadedAt, List<Video> Videos);

    private sealed record Envelope<T>(T? Response);

    private sealed record Anime
    {
        public int AnimeId { get; init; }
        public string? Title { get; init; }
        public int? Year { get; init; }
        public AnimeType? Type { get; init; }
        public PosterSet? Poster { get; init; }
        public RemoteIdSet? RemoteIds { get; init; }
    }

    private sealed record AnimeType(string? Alias);

    private sealed record PosterSet(string? Fullsize);

    private sealed record RemoteIdSet(int? ShikimoriId);

    private sealed record Video
    {
        public string? Number { get; init; }
        public string? IframeUrl { get; init; }
        public VideoData? Data { get; init; }
    }

    private sealed record VideoData(string? Player, string? Dubbing, int? PlayerId);
}
