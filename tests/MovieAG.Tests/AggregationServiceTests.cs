using Microsoft.Extensions.Logging.Abstractions;
using MovieAG.Core;

namespace MovieAG.Tests;

public class AggregationServiceTests
{
    private sealed class FakeSource(string id, Func<IReadOnlyList<SourceTitle>>? search = null, Func<IReadOnlyList<DubVariant>>? variants = null) : IAnimeSource
    {
        public string Id => id;

        public Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct) => Task.FromResult(search!());

        public Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct) => Task.FromResult(search!());

        public Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct) => Task.FromResult(variants!());

        public Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct) =>
            Task.FromResult<Playback?>(new Playback(PlaybackKind.Hls, "https://x/" + episode, []));

        public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeProvider(params IAnimeSource[] sources) : ISourceProvider
    {
        public IReadOnlyList<IAnimeSource> Active => sources;
    }

    private sealed class FakeCatalog(bool fail = false, params CatalogTitle[] titles) : ICatalog
    {
        public Task<IReadOnlyList<CatalogTitle>> SearchAsync(string query, CancellationToken ct) => Respond(titles);

        public Task<IReadOnlyList<CatalogTitle>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
            Respond(titles.Where(t => ids.Contains(t.Id)).ToList());

        public Task<CatalogTitle?> GetAsync(int id, CancellationToken ct) =>
            Task.FromResult(titles.FirstOrDefault(t => t.Id == id));

        private Task<IReadOnlyList<CatalogTitle>> Respond(IReadOnlyList<CatalogTitle> result) =>
            fail ? Task.FromException<IReadOnlyList<CatalogTitle>>(new HttpRequestException("down")) : Task.FromResult(result);
    }

    private static SourceTitle Hit(int id, string source) => new() { ShikimoriId = id, SourceId = source, Name = "Из источника", DubNames = ["Dub"] };

    private static AggregationService Service(ICatalog catalog, params IAnimeSource[] sources) =>
        new(new FakeProvider(sources), catalog, NullLogger<AggregationService>.Instance);

    [Fact]
    public async Task Search_ReportsFailedSourceAndKeepsOthers()
    {
        var good = new FakeSource("good", () => [Hit(1, "good")]);
        var bad = new FakeSource("bad", () => throw new HttpRequestException("down"));

        var result = await Service(new FakeCatalog(), good, bad).SearchAsync("q", CancellationToken.None);

        Assert.Equal(1, Assert.Single(result.Items).Id);
        Assert.Equal(["bad"], result.FailedSources);
        Assert.Equal(["good", "bad"], result.QueriedSources);
    }

    [Fact]
    public async Task Search_WorksWithoutCatalog()
    {
        var source = new FakeSource("good", () => [Hit(1, "good")]);

        var result = await Service(new FakeCatalog(fail: true), source).SearchAsync("q", CancellationToken.None);

        Assert.Equal("Из источника", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task Search_LooksUpCatalogForTitlesMissingFromCatalogSearch()
    {
        var source = new FakeSource("good", () => [Hit(7, "good")]);
        var catalog = new FakeCatalog(false, new CatalogTitle { Id = 7, Name = "Каталог" });

        var result = await Service(catalog, source).SearchAsync("q", CancellationToken.None);

        Assert.Equal("Каталог", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task GetTitle_ReturnsNullForUnknownId()
    {
        Assert.Null(await Service(new FakeCatalog()).GetTitleAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task GetTitle_GroupsVariantsAndReportsFailures()
    {
        var catalog = new FakeCatalog(false, new CatalogTitle { Id = 1, Name = "T" });
        var good = new FakeSource("good", variants: () => [new DubVariant("good", "d", "Dub", DubKind.Voice, [1])]);
        var bad = new FakeSource("bad", variants: () => throw new TaskCanceledException());

        var details = await Service(catalog, good, bad).GetTitleAsync(1, CancellationToken.None);

        Assert.Equal("Dub", Assert.Single(details!.Dubs).Name);
        Assert.Equal(["bad"], details.FailedSources);
    }

    [Fact]
    public async Task GetPlayback_ReturnsNullForInactiveSource()
    {
        var catalog = new FakeCatalog(false, new CatalogTitle { Id = 1, Name = "T" });

        Assert.Null(await Service(catalog).GetPlaybackAsync(1, "missing", "d", 1, CancellationToken.None));
    }
}
