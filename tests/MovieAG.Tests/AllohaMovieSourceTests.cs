using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>
/// Формат ответа взят из фикстур открытого клиента anime-dl-core, ответ с ошибкой токена снят с живого API 2026-11-10.
/// С настоящим токеном Alloha эти тесты не сверялись.
/// </summary>
public class AllohaMovieSourceTests
{
    private const string GreenMile = "tt0120689";

    private const string MovieJson = """
        {"status":"success","data":{"name":"Зеленая миля","original_name":"The Green Mile","year":1999,"category":1,
          "id_imdb":"tt0120689","genre":"драма, фэнтези, криминал","time":"03:09","poster":"https://alloha.test/p.jpg",
          "description":"Описание","iframe":"https://player.test/?token_movie=m&token=secret",
          "translation_iframe":{
            "87":{"name":"Профессиональный многоголосый","iframe":"https://player.test/?token_movie=m&translation=87&token=secret","quality":"BDRip"},
            "66":{"name":"Дублированный","iframe":"//player.test/?token_movie=m&translation=66&token=secret","quality":""}}}}
        """;

    private const string SeriesJson = """
        {"status":"success","data":{"name":"Сериал","year":2013,"category":4,"id_imdb":"tt2560140",
          "iframe":"https://player.test/serial","seasons":{"1":{"season":1,"episodes":{}}}}}
        """;

    private const string NameSearchJson = """
        {"status":"success","data":[
          {"name":"Зеленая миля","year":1999,"id_imdb":"tt0120689","iframe":"https://player.test/1"},
          {"name":"Сериал","id_imdb":"tt2560140","seasons":{"1":{}}},
          {"name":"Без IMDb","iframe":"https://player.test/2"}]}
        """;

    private const string NotFoundJson = """{"status":"error","error_info":"not found"}""";
    private const string BadTokenJson = """{"status":"error","error_info":"not valid token"}""";

    [Fact]
    public async Task Find_MapsMetadataAndStreamPerTranslation()
    {
        var handler = new StubHandler(("alloha.test", MovieJson));

        var movie = await Create(handler).FindAsync(GreenMile, CancellationToken.None);

        Assert.Equal("Зеленая миля", movie!.Title.Name);
        Assert.Equal(189, movie.Title.RuntimeMinutes);
        Assert.Equal(["драма", "фэнтези", "криминал"], movie.Title.Genres);
        Assert.Equal(["87", "66"], movie.Streams.Select(s => s.StreamId));
        Assert.Equal("Профессиональный многоголосый · BDRip", movie.Streams[0].Label);
        Assert.Equal("Дублированный", movie.Streams[1].Label);

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("token=secret", query);
        Assert.Contains($"imdb={GreenMile}", query);
    }

    [Fact]
    public async Task Playback_ReturnsTranslationIframe()
    {
        var handler = new StubHandler(("alloha.test", MovieJson));

        var playback = await Create(handler).GetPlaybackAsync(GreenMile, "66", CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal("https://player.test/?token_movie=m&translation=66&token=secret", playback.Url);
    }

    [Fact]
    public async Task Playback_NullForUnknownTranslation()
    {
        var handler = new StubHandler(("alloha.test", MovieJson));

        Assert.Null(await Create(handler).GetPlaybackAsync(GreenMile, "999", CancellationToken.None));
    }

    [Fact]
    public async Task Find_SkipsSeries()
    {
        var handler = new StubHandler(("alloha.test", SeriesJson));

        Assert.Null(await Create(handler).FindAsync("tt2560140", CancellationToken.None));
    }

    [Fact]
    public async Task Find_NullWhenNotFound()
    {
        var handler = new StubHandler(("alloha.test", NotFoundJson));

        Assert.Null(await Create(handler).FindAsync(GreenMile, CancellationToken.None));
    }

    [Fact]
    public async Task SearchByName_KeepsOnlyMoviesWithImdbId()
    {
        var handler = new StubHandler(("alloha.test", NameSearchJson));

        var title = Assert.Single(await Create(handler).SearchAsync("миля", CancellationToken.None));

        Assert.Equal(GreenMile, title.ImdbId);
        Assert.Contains("name=", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task RejectedToken_ThrowsWithoutExposingIt()
    {
        var handler = new StubHandler(("alloha.test", BadTokenJson));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler).PingAsync(CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public async Task InvalidImdbId_IsRejectedWithoutRequests()
    {
        var handler = new StubHandler();

        Assert.Null(await Create(handler).FindAsync("bad", CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void Readiness_RequiresToken()
    {
        var withoutToken = new MovieSourceDefinition { Id = "alloha", Type = "balancer" };
        var withToken = new MovieSourceDefinition { Id = "alloha", Type = "balancer", Token = "secret" };

        Assert.Equal(SourceReadiness.NeedsToken, SourceRegistry.GetReadiness(withoutToken));
        Assert.Equal(SourceReadiness.Ready, SourceRegistry.GetReadiness(withToken));
    }

    private static AllohaMovieSource Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new MovieSourceDefinition { Id = "alloha", Type = "balancer", Url = "https://alloha.test", Token = "secret" });
}
