using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>
/// Ответы /get-player сняты с живого Kodik 2026-11-10.
/// Ответы /search составлены по описанию API: полный режим без собственного токена вживую не проверялся.
/// </summary>
public class KodikMovieSourceTests
{
    private const string SpiritedAway = "tt0245429";

    private const string PlayerJson = """
        {"found":true,"allowed":1,"quality":null,"translation":null,"link":"//kodik.test/video/34204/abc/720p"}
        """;

    private const string NotFoundJson = """
        {"found":false,"allowed":1,"quality":null,"translation":null,"link":null}
        """;

    private const string SearchJson = """
        {"results":[
          {"id":"movie-1","type":"anime","link":"//kodik.test/video/1/a/720p","title":"Унесённые призраками","title_orig":"Sen to Chihiro no kamikakushi",
           "year":2001,"imdb_id":"tt0245429","translation":{"id":610,"title":"AniLibria","type":"voice"},
           "material_data":{"poster_url":"https://kodik.test/p.jpg","description":"Описание","genres":["аниме","фэнтези"],"duration":125}},
          {"id":"movie-2","type":"anime","link":"//kodik.test/video/1/b/720p","title":"Унесённые призраками","imdb_id":"tt0245429",
           "translation":{"id":1,"title":"Crunchyroll","type":"subtitles"}},
          {"id":"movie-3","type":"foreign-movie","link":"//kodik.test/video/2/c/720p","title":"Без IMDb",
           "translation":{"id":2,"title":"Дубляж","type":"voice"}}
        ]}
        """;

    [Fact]
    public async Task PublicToken_FindsAnimeMovieByImdbIdWithSinglePlayerStream()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));

        var movie = await CreatePublic(handler).FindAsync(SpiritedAway, CancellationToken.None);

        var stream = Assert.Single(movie!.Streams);
        Assert.Equal(KodikMovieSource.PlayerStreamId, stream.StreamId);
        Assert.Equal(SpiritedAway, movie.Title.ImdbId);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains($"imdbID={SpiritedAway}", request.RequestUri!.Query);
    }

    [Fact]
    public async Task PublicToken_FindReturnsNullWhenKodikHasNoPlayer()
    {
        var handler = new StubHandler(("/get-player", NotFoundJson));

        Assert.Null(await CreatePublic(handler).FindAsync("tt0816692", CancellationToken.None));
    }

    [Fact]
    public async Task PublicToken_PlaybackIsPlayerIframe()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));

        var playback = await CreatePublic(handler).GetPlaybackAsync(SpiritedAway, KodikMovieSource.PlayerStreamId, CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal("https://kodik.test/video/34204/abc/720p", playback.Url);
    }

    [Fact]
    public async Task PublicToken_SearchUnderstandsOnlyImdbId()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));
        var source = CreatePublic(handler);

        Assert.Empty(await source.SearchAsync("Унесённые призраками", CancellationToken.None));
        Assert.Empty(await source.GetLatestAsync(10, CancellationToken.None));
        Assert.Empty(handler.Requests);

        var found = Assert.Single(await source.SearchAsync(SpiritedAway, CancellationToken.None));
        Assert.Equal(SpiritedAway, found.ImdbId);
    }

    [Fact]
    public async Task OwnToken_FindsMovieWithStreamPerTranslationAndMetadata()
    {
        var handler = new StubHandler(("/search", SearchJson));

        var movie = await CreateWithToken(handler).FindAsync(SpiritedAway, CancellationToken.None);

        Assert.Equal("Унесённые призраками", movie!.Title.Name);
        Assert.Equal(125, movie.Title.RuntimeMinutes);
        Assert.Equal(["аниме", "фэнтези"], movie.Title.Genres);
        Assert.Equal(["610", "1"], movie.Streams.Select(s => s.StreamId));
        Assert.Equal("Crunchyroll · субтитры", movie.Streams[1].Label);

        var query = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("token=secret", query);
        Assert.Contains($"imdb_id={SpiritedAway}", query);
    }

    [Fact]
    public async Task OwnToken_PlaybackPicksTranslationLink()
    {
        var handler = new StubHandler(("/search", SearchJson));

        var playback = await CreateWithToken(handler).GetPlaybackAsync(SpiritedAway, "1", CancellationToken.None);

        Assert.Equal("https://kodik.test/video/1/b/720p", playback!.Url);
    }

    [Fact]
    public async Task OwnToken_SearchByTitleSkipsResultsWithoutImdbId()
    {
        var handler = new StubHandler(("/search", SearchJson));

        var title = Assert.Single(await CreateWithToken(handler).SearchAsync("призраками", CancellationToken.None));

        Assert.Equal(SpiritedAway, title.ImdbId);
        Assert.Contains("title=", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task OwnToken_LatestUsesList()
    {
        var handler = new StubHandler(("/list", SearchJson));

        Assert.Single(await CreateWithToken(handler).GetLatestAsync(10, CancellationToken.None));
        Assert.Contains("sort=updated_at", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task InvalidImdbId_IsRejectedWithoutRequests()
    {
        var handler = new StubHandler();

        Assert.Null(await CreatePublic(handler).FindAsync("../etc", CancellationToken.None));
        Assert.Null(await CreatePublic(handler).GetPlaybackAsync("bad", KodikMovieSource.PlayerStreamId, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private static KodikMovieSource CreatePublic(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new MovieSourceDefinition { Id = "kodik-movies", Type = "balancer", Url = "https://kodik.test" }, new FixedToken());

    private static KodikMovieSource CreateWithToken(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new MovieSourceDefinition { Id = "kodik-movies", Type = "balancer", Url = "https://kodik.test", Token = "secret" });

    private sealed class FixedToken : IKodikTokenProvider
    {
        public ValueTask<string> GetTokenAsync(CancellationToken ct) => ValueTask.FromResult("public1");

        public void Invalidate(string rejectedToken)
        {
        }
    }
}
