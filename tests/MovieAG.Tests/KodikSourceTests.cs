using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>
/// Ответы составлены по стороннему описанию API, без реального токена.
/// Живой ответ Kodik с этими тестами ещё не сверялся.
/// </summary>
public class KodikSourceTests
{
    private const string SerialJson = """
        {"time":"1ms","total":3,"results":[
          {"id":"serial-1","type":"anime-serial","link":"//kodik.test/serial/1/a/720p","title":"Фрирен","title_orig":"Sousou no Frieren",
           "translation":{"id":610,"title":"AniLibria.TV","type":"voice"},"year":2023,"last_season":1,"last_episode":28,"episodes_count":28,
           "shikimori_id":"52991","material_data":{"anime_poster_url":"https://kodik.test/p.jpg"},
           "seasons":{"1":{"link":"//kodik.test/season/1","episodes":{"2":"//kodik.test/ep/2","1":"//kodik.test/ep/1"}}}},
          {"id":"serial-2","type":"anime-serial","link":"//kodik.test/serial/2/b/720p","title":"Фрирен",
           "translation":{"id":1,"title":"Crunchyroll.Subtitles","type":"subtitles"},"last_season":1,"shikimori_id":52991,
           "seasons":{"1":{"link":"//kodik.test/season/2","episodes":{"1":{"link":"https://kodik.test/ep/sub1","title":null}}}}},
          {"id":"serial-3","type":"anime-serial","link":"//kodik.test/serial/3/c/720p","title":"Без каталога",
           "translation":{"id":2,"title":"Другая","type":"voice"},"shikimori_id":null}
        ]}
        """;

    private const string MovieJson = """
        {"results":[
          {"id":"movie-1","type":"anime","link":"//kodik.test/video/1/a/720p","title":"Фильм",
           "translation":{"id":5,"title":"AniDUB","type":"voice"},"shikimori_id":"100"}
        ]}
        """;

    private static readonly CatalogTitle Frieren = new() { Id = 52991, Name = "Фрирен" };
    private static readonly CatalogTitle Movie = new() { Id = 100, Name = "Фильм" };

    private static KodikSource Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new SourceDefinition { Id = "kodik", Type = "balancer", Url = "https://kodik.test", Token = "secret" });

    [Fact]
    public async Task Search_GroupsTranslationsAndSkipsTitlesWithoutShikimoriId()
    {
        var handler = new StubHandler(("/search", SerialJson));

        var title = Assert.Single(await Create(handler).SearchAsync("frieren", CancellationToken.None));

        Assert.Equal(52991, title.ShikimoriId);
        Assert.Equal(["AniLibria.TV", "Crunchyroll.Subtitles"], title.DubNames);
        Assert.Equal("tv", title.Kind);
        Assert.Equal("https://kodik.test/p.jpg", title.PosterUrl);
    }

    [Fact]
    public async Task Search_SendsTokenAndAnimeTypes()
    {
        var handler = new StubHandler(("/search", SerialJson));

        await Create(handler).SearchAsync("frieren", CancellationToken.None);

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("token=secret", query);
        Assert.Contains("types=anime%2Canime-serial", query);
    }

    [Fact]
    public async Task GetVariants_MapsEpisodesAndSubtitleKind()
    {
        var handler = new StubHandler(("/search", SerialJson));

        var variants = await Create(handler).GetVariantsAsync(Frieren, CancellationToken.None);

        Assert.Equal(2, variants.Count);
        Assert.Equal([1, 2], variants[0].Episodes);
        Assert.Equal(DubKind.Voice, variants[0].Kind);
        Assert.Equal(DubKind.Subtitles, variants[1].Kind);
    }

    [Fact]
    public async Task GetVariants_TreatsMovieAsOneEpisode()
    {
        var handler = new StubHandler(("/search", MovieJson));

        var variant = Assert.Single(await Create(handler).GetVariantsAsync(Movie, CancellationToken.None));

        Assert.Equal([1], variant.Episodes);
    }

    [Theory]
    [InlineData("610", 2, "https://kodik.test/ep/2")]
    [InlineData("1", 1, "https://kodik.test/ep/sub1")]
    public async Task GetPlayback_ReturnsEpisodeIframe(string dubId, int episode, string expected)
    {
        var handler = new StubHandler(("/search", SerialJson));

        var playback = await Create(handler).GetPlaybackAsync(Frieren, dubId, episode, CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal(expected, playback.Url);
    }

    [Fact]
    public async Task GetPlayback_ReturnsMovieLink()
    {
        var handler = new StubHandler(("/search", MovieJson));

        var playback = await Create(handler).GetPlaybackAsync(Movie, "5", 1, CancellationToken.None);

        Assert.Equal("https://kodik.test/video/1/a/720p", playback!.Url);
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForUnknownDubOrEpisode()
    {
        var handler = new StubHandler(("/search", SerialJson));
        var source = Create(handler);

        Assert.Null(await source.GetPlaybackAsync(Frieren, "999", 1, CancellationToken.None));
        Assert.Null(await source.GetPlaybackAsync(Frieren, "610", 99, CancellationToken.None));
    }

    [Fact]
    public async Task Errors_DoNotExposeToken()
    {
        var handler = new StubHandler { ForcedStatus = System.Net.HttpStatusCode.Unauthorized };

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).PingAsync(CancellationToken.None));

        Assert.DoesNotContain("secret", ex.Message);
    }
}
