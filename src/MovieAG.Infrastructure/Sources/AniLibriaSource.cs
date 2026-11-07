using System.Net.Http.Json;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>Публичное API AniLibria: у каждого релиза одна своя озвучка и прямые HLS-ссылки.</summary>
public sealed class AniLibriaSource(HttpClient http, SourceDefinition definition) : IAnimeSource
{
    public const string AdapterName = "anilibria";
    private const string DefaultUrl = "https://anilibria.top";
    private const string DubName = "AniLibria";

    private readonly string _baseUrl = (definition.Url ?? DefaultUrl).TrimEnd('/');

    public string Id => definition.Id;

    public async Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var releases = await SearchReleasesAsync(query, ct);
        return releases.Where(r => r.Shikimori?.Id is not null).Select(ToSourceTitle).ToList();
    }

    public async Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        var releases = await GetAsync<List<Release>>($"/api/v1/anime/releases/latest?limit={limit}", ct);
        return releases.Where(r => r.Shikimori?.Id is not null).Select(ToSourceTitle).ToList();
    }

    public async Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct)
    {
        var release = await FindReleaseAsync(title, ct);
        if (release is null) return [];

        var episodes = PlayableEpisodes(release).Select(e => (int)e.Ordinal).ToList();
        return episodes.Count == 0
            ? []
            : [new DubVariant(Id, release.Id.ToString(), DubName, DubKind.Voice, episodes)];
    }

    public async Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct)
    {
        // Ссылки на поток не кэшируем: за ними каждый раз идём в API.
        var release = await GetAsync<Release>($"/api/v1/anime/releases/{Uri.EscapeDataString(dubId)}", ct);
        var match = PlayableEpisodes(release).FirstOrDefault(e => (int)e.Ordinal == episode);
        if (match is null) return null;

        var qualities = Qualities(match);
        return new Playback(PlaybackKind.Hls, qualities[0].Url, qualities);
    }

    public Task PingAsync(CancellationToken ct) => GetAsync<List<Release>>("/api/v1/anime/releases/latest?limit=1", ct);

    private async Task<Release?> FindReleaseAsync(CatalogTitle title, CancellationToken ct)
    {
        var queries = new[] { title.OriginalName, title.Name }
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct();

        foreach (var query in queries)
        {
            var found = (await SearchReleasesAsync(query!, ct)).FirstOrDefault(r => r.Shikimori?.Id == title.Id);
            if (found is not null)
            {
                return await GetAsync<Release>($"/api/v1/anime/releases/{found.Id}", ct);
            }
        }

        return null;
    }

    private Task<List<Release>> SearchReleasesAsync(string query, CancellationToken ct) =>
        GetAsync<List<Release>>($"/api/v1/app/search/releases?query={Uri.EscapeDataString(query)}", ct);

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(_baseUrl + path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(SourceJson.Options, ct)
            ?? throw new InvalidOperationException("Пустой ответ AniLibria");
    }

    private SourceTitle ToSourceTitle(Release release) => new()
    {
        ShikimoriId = release.Shikimori!.Id!.Value,
        SourceId = Id,
        Name = release.Name?.Main ?? release.Name?.English ?? release.Alias ?? release.Id.ToString(),
        OriginalName = release.Name?.English,
        Kind = SourceJson.NormalizeKind(release.Type?.Value),
        Year = release.Year,
        EpisodesTotal = release.EpisodesTotal,
        PosterUrl = release.Poster?.Src is { Length: > 0 } src ? _baseUrl + src : null,
        DubNames = [DubName],
        LatestEpisode = release.LatestEpisode is { } latest ? (int)latest.Ordinal : null
    };

    private static IEnumerable<Episode> PlayableEpisodes(Release release) =>
        (release.Episodes ?? [])
            .Where(e => e.Ordinal == Math.Floor(e.Ordinal) && Qualities(e).Count > 0)
            .OrderBy(e => e.Ordinal);

    private static List<PlaybackQuality> Qualities(Episode episode)
    {
        var qualities = new List<PlaybackQuality>(3);
        if (!string.IsNullOrEmpty(episode.Hls1080)) qualities.Add(new("1080p", episode.Hls1080));
        if (!string.IsNullOrEmpty(episode.Hls720)) qualities.Add(new("720p", episode.Hls720));
        if (!string.IsNullOrEmpty(episode.Hls480)) qualities.Add(new("480p", episode.Hls480));
        return qualities;
    }

    private sealed record Release
    {
        public int Id { get; init; }
        public ValueDescription? Type { get; init; }
        public int? Year { get; init; }
        public ReleaseName? Name { get; init; }
        public string? Alias { get; init; }
        public ExternalRef? Shikimori { get; init; }
        public PosterRef? Poster { get; init; }
        public int? EpisodesTotal { get; init; }
        public List<Episode>? Episodes { get; init; }
        public Episode? LatestEpisode { get; init; }
    }

    private sealed record ValueDescription(string? Value);

    private sealed record ReleaseName(string? Main, string? English);

    private sealed record ExternalRef(int? Id);

    private sealed record PosterRef(string? Src);

    private sealed record Episode
    {
        public double Ordinal { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("hls_480")]
        public string? Hls480 { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("hls_720")]
        public string? Hls720 { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("hls_1080")]
        public string? Hls1080 { get; init; }
    }
}
