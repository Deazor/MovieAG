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

    /// <summary>Ответ /get-player в формате, снятом с живого Kodik 2026-11-10.</summary>
    private const string PlayerJson = """
        {"found":true,"allowed":1,"quality":"WEB-DLRip 720p","translation":"AnimeVost","link":"//kodik.test/serial/53973/abc/720p"}
        """;

    private const string MoviePlayerJson = """
        {"found":true,"allowed":1,"translation":"AniDUB","link":"//kodik.test/video/1/a/720p"}
        """;

    private static readonly CatalogTitle FrierenWithEpisodes = Frieren with { EpisodesTotal = 28 };

    [Fact]
    public async Task PublicToken_GetVariants_ReturnsSinglePlayerVariantFromGetPlayer()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));

        var variant = Assert.Single(await CreatePublic(handler, new FakeTokens("public1"))
            .GetVariantsAsync(FrierenWithEpisodes, CancellationToken.None));

        Assert.Equal(KodikSource.PlayerDubId, variant.DubId);
        Assert.Contains("AnimeVost", variant.DubName);
        Assert.Equal(Enumerable.Range(1, 28), variant.Episodes);

        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("token=public1", request.RequestUri!.Query);
        Assert.Contains("shikimoriID=52991", request.RequestUri.Query);
    }

    [Fact]
    public async Task PublicToken_GetVariants_EmptyWhenPlayerNotFound()
    {
        var handler = new StubHandler(("/get-player", """{"found":false}"""));

        Assert.Empty(await CreatePublic(handler, new FakeTokens("public1")).GetVariantsAsync(Frieren, CancellationToken.None));
    }

    [Fact]
    public async Task PublicToken_GetPlayback_AddsEpisodeToSerialLink()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));

        var playback = await CreatePublic(handler, new FakeTokens("public1"))
            .GetPlaybackAsync(Frieren, KodikSource.PlayerDubId, 3, CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal("https://kodik.test/serial/53973/abc/720p?episode=3", playback.Url);
    }

    [Fact]
    public async Task PublicToken_MovieIsOneEpisodeWithoutEpisodeParameter()
    {
        var handler = new StubHandler(("/get-player", MoviePlayerJson));
        var source = CreatePublic(handler, new FakeTokens("public1"));

        var variant = Assert.Single(await source.GetVariantsAsync(Movie, CancellationToken.None));
        var playback = await source.GetPlaybackAsync(Movie, KodikSource.PlayerDubId, 1, CancellationToken.None);

        Assert.Equal([1], variant.Episodes);
        Assert.Equal("https://kodik.test/video/1/a/720p", playback!.Url);
    }

    [Fact]
    public async Task PublicToken_SearchAndLatestAreEmptyWithoutRequests()
    {
        var handler = new StubHandler();
        var source = CreatePublic(handler, new FakeTokens("public1"));

        Assert.Empty(await source.SearchAsync("frieren", CancellationToken.None));
        Assert.Empty(await source.GetLatestAsync(10, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PublicToken_PingUsesGetPlayer()
    {
        var handler = new StubHandler(("/get-player", PlayerJson));

        await CreatePublic(handler, new FakeTokens("public1")).PingAsync(CancellationToken.None);

        Assert.Contains("/get-player", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task ConfiguredToken_TakesPriorityOverPublic()
    {
        var handler = new StubHandler(("/search", SerialJson));
        var tokens = new FakeTokens("public1");
        var definition = new SourceDefinition { Id = "kodik", Type = "balancer", Url = "https://kodik.test", Token = "secret" };

        await new KodikSource(new HttpClient(handler), definition, tokens).SearchAsync("frieren", CancellationToken.None);

        Assert.Contains("token=secret", handler.Requests[0].RequestUri!.Query);
        Assert.Equal(0, tokens.Requested);
    }

    [Fact]
    public async Task RejectedPublicToken_IsRefreshedAndRequestRetriedOnce()
    {
        var handler = new TokenAwareHandler(validToken: "public2");
        var tokens = new FakeTokens("public1", "public2");

        var variants = await CreatePublic(handler, tokens).GetVariantsAsync(Frieren, CancellationToken.None);

        Assert.Single(variants);
        Assert.Equal(["public1"], tokens.Invalidated);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task RejectedPublicToken_FailsAfterSingleRetry()
    {
        var handler = new TokenAwareHandler(validToken: "never");
        var tokens = new FakeTokens("public1", "public2", "public3");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => CreatePublic(handler, tokens).PingAsync(CancellationToken.None));

        Assert.Equal(2, handler.Calls);
        Assert.DoesNotContain("public", ex.Message);
    }

    [Fact]
    public async Task OtherErrors_AreNotRetried()
    {
        var handler = new StubHandler { ForcedStatus = System.Net.HttpStatusCode.InternalServerError };
        var tokens = new FakeTokens("public1", "public2");

        await Assert.ThrowsAsync<HttpRequestException>(() => CreatePublic(handler, tokens).PingAsync(CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Empty(tokens.Invalidated);
    }

    [Fact]
    public async Task WithoutAnyToken_Throws()
    {
        var source = new KodikSource(new HttpClient(new StubHandler()), new SourceDefinition { Id = "kodik", Type = "balancer" });

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.PingAsync(CancellationToken.None));
    }

    private static KodikSource CreatePublic(HttpMessageHandler handler, IKodikTokenProvider tokens) =>
        new(new HttpClient(handler), new SourceDefinition { Id = "kodik", Type = "balancer", Url = "https://kodik.test" }, tokens);

    /// <summary>Выдаёт токены по очереди: каждый Invalidate переключает на следующий.</summary>
    private sealed class FakeTokens(params string[] tokens) : IKodikTokenProvider
    {
        private int _index;

        public int Requested { get; private set; }

        public List<string> Invalidated { get; } = [];

        public ValueTask<string> GetTokenAsync(CancellationToken ct)
        {
            Requested++;
            return ValueTask.FromResult(tokens[_index]);
        }

        public void Invalidate(string rejectedToken)
        {
            Invalidated.Add(rejectedToken);
            _index = Math.Min(_index + 1, tokens.Length - 1);
        }
    }

    /// <summary>Как живой Kodik: на чужой токен отвечает 401 с полем error.</summary>
    private sealed class TokenAwareHandler(string validToken) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var ok = request.RequestUri!.Query.Contains($"token={validToken}", StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(ok ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    ok ? PlayerJson : """{"error":"Отсутствует или неверный токен"}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
