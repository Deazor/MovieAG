using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>Ответы сокращены из реальных ответов api.yani.tv (ноябрь 2026).</summary>
public class YummySourceTests
{
    private const string SearchJson = """
        {"response":[
          {"anime_id":10661,"title":"Провожающая в последний путь Фрирен","year":2023,
           "type":{"name":"Сериал","alias":"tv"},"poster":{"fullsize":"//static.yani.tv/posters/full/1.jpg"},
           "remote_ids":{"shikimori_id":52991,"myanimelist_id":52991}},
          {"anime_id":2,"title":"Без Shikimori","remote_ids":{"shikimori_id":0}},
          {"anime_id":3,"title":"Без внешних ID"}
        ]}
        """;

    private const string VideosJson = """
        {"response":[
          {"number":"1","iframe_url":"https://player.aksor.tv/video/1","data":{"player":"Плеер Aksor","dubbing":"Озвучка AniLibria","player_id":1}},
          {"number":"1","iframe_url":"//kodikplayer.com/season/1?episode=1","data":{"player":"Плеер Kodik","dubbing":"Озвучка AniLibria","player_id":4}},
          {"number":"2","iframe_url":"//kodikplayer.com/season/1?episode=2","data":{"player":"Плеер Kodik","dubbing":"Озвучка AniLibria","player_id":4}},
          {"number":"12.5","iframe_url":"//kodikplayer.com/special","data":{"player":"Плеер Kodik","dubbing":"Озвучка AniLibria","player_id":4}},
          {"number":"1","iframe_url":"https://alloha.test/1","data":{"player":"Плеер Alloha","dubbing":"Озвучка AniDUB","player_id":2}},
          {"number":"1","iframe_url":"//kodikplayer.com/season/2?episode=1","data":{"player":"Плеер Kodik","dubbing":"Субтитры Crunchyroll","player_id":4}},
          {"number":"","iframe_url":"//kodikplayer.com/video/9","data":{"player":"Плеер Kodik","dubbing":"Озвучка Фильм","player_id":4}}
        ]}
        """;

    private static readonly CatalogTitle Frieren = new() { Id = 52991, Name = "Фрирен", OriginalName = "Sousou no Frieren" };

    private static YummySource Create(StubHandler handler, string? token = null) =>
        new(new HttpClient(handler), new SourceDefinition { Id = "yummy", Type = "api", Url = "https://y.test/", Token = token });

    private static StubHandler Routes() => new(
        ("/search", SearchJson),
        ("/anime/10661/videos", VideosJson),
        ("/anime?limit=1", SearchJson));

    [Fact]
    public async Task Search_MapsTitlesAndSkipsThoseWithoutShikimoriId()
    {
        var title = Assert.Single(await Create(Routes()).SearchAsync("frieren", CancellationToken.None));

        Assert.Equal(52991, title.ShikimoriId);
        Assert.Equal("tv", title.Kind);
        Assert.Equal("https://static.yani.tv/posters/full/1.jpg", title.PosterUrl);
        Assert.Empty(title.DubNames);
    }

    [Fact]
    public async Task Search_SkipsRequestForQueryTheApiWouldReject()
    {
        var handler = Routes();

        Assert.Empty(await Create(handler).SearchAsync("аб", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetVariants_SplitsByDubAndPlayerWithPreferredPlayerFirst()
    {
        var variants = await Create(Routes()).GetVariantsAsync(Frieren, CancellationToken.None);

        var aniLibria = variants.Where(v => v.DubName == "AniLibria").ToList();
        Assert.Equal(["Kodik", "Aksor"], aniLibria.Select(v => v.Label));
        Assert.Equal([1, 2], aniLibria[0].Episodes);
        Assert.Equal([1], aniLibria[1].Episodes);
    }

    [Fact]
    public async Task GetVariants_DetectsSubtitlesAndKeepsTheirName()
    {
        var variants = await Create(Routes()).GetVariantsAsync(Frieren, CancellationToken.None);

        var subtitles = Assert.Single(variants, v => v.Kind == DubKind.Subtitles);
        Assert.Equal("Субтитры Crunchyroll", subtitles.DubName);
    }

    [Fact]
    public async Task GetVariants_TreatsEmptyNumberAsSingleEpisode()
    {
        var variants = await Create(Routes()).GetVariantsAsync(Frieren, CancellationToken.None);

        Assert.Equal([1], Assert.Single(variants, v => v.DubName == "Фильм").Episodes);
    }

    [Fact]
    public async Task GetVariants_ReturnsEmptyWhenShikimoriIdDoesNotMatch()
    {
        Assert.Empty(await Create(Routes()).GetVariantsAsync(Frieren with { Id = 1000 }, CancellationToken.None));
    }

    [Fact]
    public async Task GetPlayback_FindsIframeForDubPlayerAndEpisode()
    {
        var source = Create(Routes());
        var variants = await source.GetVariantsAsync(Frieren, CancellationToken.None);
        var kodik = variants.First(v => v.DubName == "AniLibria" && v.Label == "Kodik");

        var playback = await source.GetPlaybackAsync(Frieren, kodik.DubId, 2, CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal("https://kodikplayer.com/season/1?episode=2", playback.Url);
    }

    [Fact]
    public async Task GetPlayback_ReusesLoadedVideosForTheSameTitle()
    {
        var handler = Routes();
        var source = Create(handler);
        var variant = (await source.GetVariantsAsync(Frieren, CancellationToken.None))[0];

        await source.GetPlaybackAsync(Frieren, variant.DubId, 1, CancellationToken.None);
        await source.GetPlaybackAsync(Frieren, variant.DubId, 2, CancellationToken.None);

        Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("мусор")]
    [InlineData("x|4|Озвучка AniLibria")]
    [InlineData("10661|y|Озвучка AniLibria")]
    public async Task GetPlayback_ReturnsNullForMalformedDubId(string dubId)
    {
        Assert.Null(await Create(Routes()).GetPlaybackAsync(Frieren, dubId, 1, CancellationToken.None));
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForMissingEpisode()
    {
        var source = Create(Routes());
        var variant = (await source.GetVariantsAsync(Frieren, CancellationToken.None))[0];

        Assert.Null(await source.GetPlaybackAsync(Frieren, variant.DubId, 99, CancellationToken.None));
    }

    [Fact]
    public async Task SendsApplicationHeaderOnlyWhenTokenIsSet()
    {
        var anonymous = Routes();
        var withToken = Routes();

        await Create(anonymous).PingAsync(CancellationToken.None);
        await Create(withToken, "client-id").PingAsync(CancellationToken.None);

        Assert.False(anonymous.Requests[0].Headers.Contains("X-Application"));
        Assert.Equal(["client-id"], withToken.Requests[0].Headers.GetValues("X-Application"));
    }
}
