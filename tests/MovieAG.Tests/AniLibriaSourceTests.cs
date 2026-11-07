using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>Ответы сокращены из реальных ответов anilibria.top (ноябрь 2026).</summary>
public class AniLibriaSourceTests
{
    private const string SearchJson = """
        [
          {"id":9542,"type":{"value":"TV","description":"ТВ"},"year":2023,
           "name":{"main":"Провожающая в последний путь Фрирен","english":"Sousou no Frieren","alternative":null},
           "alias":"sousou-no-frieren","shikimori":{"id":52991,"url":"https://shikimori.io/animes/52991"},
           "poster":{"src":"/storage/releases/posters/9542/a.jpg"},"episodes_total":28},
          {"id":1,"type":{"value":"TV"},"year":2000,"name":{"main":"Без ID"},"alias":"x","shikimori":null,"poster":{"src":null}}
        ]
        """;

    private const string DetailJson = """
        {"id":9542,"type":{"value":"TV"},"year":2023,"name":{"main":"Фрирен","english":"Sousou no Frieren"},
         "shikimori":{"id":52991},"episodes_total":3,
         "episodes":[
           {"ordinal":2,"hls_480":"https://cdn/2-480.m3u8","hls_720":"https://cdn/2-720.m3u8","hls_1080":null},
           {"ordinal":1,"hls_480":"https://cdn/1-480.m3u8","hls_720":"https://cdn/1-720.m3u8","hls_1080":"https://cdn/1-1080.m3u8"},
           {"ordinal":1.5,"hls_480":"https://cdn/15.m3u8"},
           {"ordinal":3,"hls_480":null,"hls_720":null,"hls_1080":null}
         ]}
        """;

    private const string LatestJson = """
        [{"id":10301,"type":{"value":"MOVIE"},"year":2026,"name":{"main":"Новое"},"shikimori":{"id":57612},
          "poster":{"src":"/p.jpg"},"latest_episode":{"ordinal":5}}]
        """;

    private static readonly CatalogTitle Frieren = new() { Id = 52991, Name = "Фрирен", OriginalName = "Sousou no Frieren" };

    private static AniLibriaSource Create(StubHandler handler) =>
        new(new HttpClient(handler), new SourceDefinition { Id = "anilibria", Type = "api", Url = "https://al.test/" });

    private static StubHandler Routes() => new(
        ("/app/search/releases", SearchJson),
        ("/releases/latest", LatestJson),
        ("/releases/9542", DetailJson));

    [Fact]
    public async Task Search_MapsReleasesAndSkipsThoseWithoutShikimoriId()
    {
        var result = await Create(Routes()).SearchAsync("frieren", CancellationToken.None);

        var title = Assert.Single(result);
        Assert.Equal(52991, title.ShikimoriId);
        Assert.Equal("tv", title.Kind);
        Assert.Equal("https://al.test/storage/releases/posters/9542/a.jpg", title.PosterUrl);
        Assert.Equal(["AniLibria"], title.DubNames);
    }

    [Fact]
    public async Task Search_EscapesQuery()
    {
        var handler = Routes();
        await Create(handler).SearchAsync("а б&c", CancellationToken.None);

        Assert.Contains("query=%D0%B0%20%D0%B1%26c", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetLatest_ReadsLatestEpisode()
    {
        var title = Assert.Single(await Create(Routes()).GetLatestAsync(1, CancellationToken.None));

        Assert.Equal(5, title.LatestEpisode);
        Assert.Equal("movie", title.Kind);
    }

    [Fact]
    public async Task GetVariants_ListsOnlyPlayableWholeEpisodesInOrder()
    {
        var variant = Assert.Single(await Create(Routes()).GetVariantsAsync(Frieren, CancellationToken.None));

        Assert.Equal("9542", variant.DubId);
        Assert.Equal([1, 2], variant.Episodes);
    }

    [Fact]
    public async Task GetVariants_ReturnsEmptyWhenShikimoriIdDoesNotMatch()
    {
        var other = Frieren with { Id = 1000 };

        Assert.Empty(await Create(Routes()).GetVariantsAsync(other, CancellationToken.None));
    }

    [Fact]
    public async Task GetPlayback_ReturnsHlsWithBestQualityFirst()
    {
        var playback = await Create(Routes()).GetPlaybackAsync(Frieren, "9542", 1, CancellationToken.None);

        Assert.Equal(PlaybackKind.Hls, playback!.Kind);
        Assert.Equal("https://cdn/1-1080.m3u8", playback.Url);
        Assert.Equal(["1080p", "720p", "480p"], playback.Qualities.Select(q => q.Label));
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForMissingEpisode()
    {
        Assert.Null(await Create(Routes()).GetPlaybackAsync(Frieren, "9542", 99, CancellationToken.None));
    }

    [Fact]
    public async Task Ping_ThrowsOnServerError()
    {
        var handler = new StubHandler { ForcedStatus = System.Net.HttpStatusCode.InternalServerError };

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).PingAsync(CancellationToken.None));
    }
}
