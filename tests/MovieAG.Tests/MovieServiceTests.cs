using Microsoft.Extensions.Logging.Abstractions;
using MovieAG.Core;

namespace MovieAG.Tests;

public class MovieServiceTests
{
    private const string Id = "tt1254207";

    private sealed class FakeSource(string id, bool fail = false, params MovieTitle[] titles) : IMovieSource
    {
        public string Id => id;

        public Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct) => Respond();

        public Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct) => Respond();

        public Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct)
        {
            if (fail) return Task.FromException<SourceMovie?>(new HttpRequestException("down"));
            var title = titles.FirstOrDefault(t => t.ImdbId == imdbId);
            return Task.FromResult(title is null ? null : new SourceMovie(title, [new MovieStream(id, "0", "Оригинал")]));
        }

        public Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct) =>
            Task.FromResult<Playback?>(new Playback(PlaybackKind.Hls, $"https://{id}/{streamId}", []));

        public Task PingAsync(CancellationToken ct) => Task.CompletedTask;

        private Task<IReadOnlyList<MovieTitle>> Respond() => fail
            ? Task.FromException<IReadOnlyList<MovieTitle>>(new HttpRequestException("down"))
            : Task.FromResult<IReadOnlyList<MovieTitle>>(titles);
    }

    private sealed class FakeProvider(params IMovieSource[] sources) : IMovieSourceProvider
    {
        public IReadOnlyList<IMovieSource> Active => sources;
    }

    private sealed class FakeCatalog(MovieTitle? title = null, bool fail = false) : IMovieCatalog
    {
        public Task<MovieTitle?> GetAsync(string imdbId, CancellationToken ct) => fail
            ? Task.FromException<MovieTitle?>(new HttpRequestException("down"))
            : Task.FromResult(title?.ImdbId == imdbId ? title : null);
    }

    private static MovieTitle Title(string name, string? description = null) =>
        new() { ImdbId = Id, Name = name, Description = description };

    private static MovieService Service(IMovieCatalog catalog, params IMovieSource[] sources) =>
        new(new FakeProvider(sources), catalog, NullLogger<MovieService>.Instance);

    [Fact]
    public async Task Search_MergesSameMovieAcrossSourcesByImdbId()
    {
        var service = Service(new FakeCatalog(), new FakeSource("a", false, Title("Кролик")), new FakeSource("b", false, Title("Bunny")));

        var result = await service.SearchAsync("кро", CancellationToken.None);

        var card = Assert.Single(result.Items);
        Assert.Equal(["a", "b"], card.Sources);
        Assert.Equal("Кролик", card.Name);
    }

    [Fact]
    public async Task Search_ReportsFailedSourceAndKeepsOthers()
    {
        var service = Service(new FakeCatalog(), new FakeSource("bad", fail: true), new FakeSource("good", false, Title("Кролик")));

        var result = await service.SearchAsync("кро", CancellationToken.None);

        Assert.Equal(["bad"], result.FailedSources);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Get_PrefersCatalogButKeepsSourceFieldsItLacks()
    {
        var catalog = new FakeCatalog(Title("Из каталога"));
        var service = Service(catalog, new FakeSource("a", false, Title("Из источника", "Описание источника")));

        var details = await service.GetAsync(Id, CancellationToken.None);

        Assert.Equal("Из каталога", details!.Movie.Name);
        Assert.Equal("Описание источника", details.Movie.Description);
    }

    [Fact]
    public async Task Get_WorksWhenCatalogIsDown()
    {
        var service = Service(new FakeCatalog(fail: true), new FakeSource("a", false, Title("Из источника")));

        var details = await service.GetAsync(Id, CancellationToken.None);

        Assert.Equal("Из источника", details!.Movie.Name);
        Assert.Single(details.Streams);
    }

    [Fact]
    public async Task Get_ShowsCatalogCardWithoutStreamsWhenNoSourceHasMovie()
    {
        var service = Service(new FakeCatalog(Title("Из каталога")), new FakeSource("a"));

        var details = await service.GetAsync(Id, CancellationToken.None);

        Assert.Equal("Из каталога", details!.Movie.Name);
        Assert.Empty(details.Streams);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenMovieIsUnknown()
    {
        var service = Service(new FakeCatalog(), new FakeSource("a"));

        Assert.Null(await service.GetAsync(Id, CancellationToken.None));
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForUnknownSource()
    {
        var service = Service(new FakeCatalog(), new FakeSource("a"));

        Assert.Null(await service.GetPlaybackAsync(Id, "missing", "0", CancellationToken.None));
    }
}
