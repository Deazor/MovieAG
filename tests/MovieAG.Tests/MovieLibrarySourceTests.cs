using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

public class MovieLibrarySourceTests
{
    private const string Bunny = "tt1254207";
    private const string Steel = "tt2285752";

    private static MovieLibrarySource Create(HttpMessageHandler? handler = null) => new(
        new HttpClient(handler ?? new StubHandler()),
        new MovieSourceDefinition
        {
            Id = "open",
            Items =
            [
                new() { ImdbId = Bunny, Name = "Большой кролик", OriginalName = "Big Buck Bunny", Url = "https://cdn/bunny.m3u8", License = "CC BY 3.0" },
                new() { ImdbId = Bunny, Name = "Большой кролик", Label = "Субтитры", Url = "https://cdn/bunny-sub.m3u8" },
                new() { ImdbId = Steel, Name = "Слёзы стали", Url = "https://cdn/steel.m3u8" }
            ]
        });

    [Theory]
    [InlineData("кролик")]
    [InlineData("buck")]
    [InlineData(Bunny)]
    public async Task Search_MatchesNamesAndImdbIdOncePerMovie(string query)
    {
        var found = await Create().SearchAsync(query, CancellationToken.None);

        Assert.Equal(Bunny, Assert.Single(found).ImdbId);
    }

    [Fact]
    public async Task Latest_RespectsLimitAndGroupsVariants()
    {
        var all = await Create().GetLatestAsync(10, CancellationToken.None);
        var one = await Create().GetLatestAsync(1, CancellationToken.None);

        Assert.Equal([Bunny, Steel], all.Select(m => m.ImdbId));
        Assert.Single(one);
    }

    [Fact]
    public async Task Find_ReturnsEveryVariantOfMovie()
    {
        var movie = await Create().FindAsync(Bunny, CancellationToken.None);

        Assert.Equal(["Оригинал", "Субтитры"], movie!.Streams.Select(s => s.Label));
        Assert.Equal("CC BY 3.0", movie.Streams[0].License);
    }

    [Fact]
    public async Task Find_ReturnsNullForUnknownMovie()
    {
        Assert.Null(await Create().FindAsync("tt0000001", CancellationToken.None));
    }

    [Fact]
    public async Task GetPlayback_ReturnsHlsOfRequestedVariant()
    {
        var source = Create();
        var streamId = (await source.FindAsync(Bunny, CancellationToken.None))!.Streams[1].StreamId;

        var playback = await source.GetPlaybackAsync(Bunny, streamId, CancellationToken.None);

        Assert.Equal(PlaybackKind.Hls, playback!.Kind);
        Assert.Equal("https://cdn/bunny-sub.m3u8", playback.Url);
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForUnknownVariant()
    {
        Assert.Null(await Create().GetPlaybackAsync(Bunny, "99", CancellationToken.None));
    }

    [Fact]
    public async Task Ping_FailsWhenStreamIsUnavailable()
    {
        await Assert.ThrowsAsync<HttpRequestException>(() => Create(new StubHandler()).PingAsync(CancellationToken.None));
    }
}
