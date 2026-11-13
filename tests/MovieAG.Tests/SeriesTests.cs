using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MovieAG.Core;
using MovieAG.Infrastructure.Catalog;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

/// <summary>
/// Форма seasons → episodes → translation взята из фикстуры сериала открытого клиента anime-dl-core.
/// С настоящим токеном Alloha эти ответы не сверялись.
/// </summary>
public class AllohaSeriesSourceTests
{
    private const string BreakingBad = "tt0903747";

    private const string SeriesJson = """
        {"status":"success","data":{"name":"Во все тяжкие","original_name":"Breaking Bad","year":2008,"category":4,
          "id_imdb":"tt0903747","genre":"драма, криминал","poster":"https://alloha.test/p.jpg","seasons_count":2,
          "rating_imdb":9.5,"rating_kp":8.9,
          "seasons":{
            "1":{"season":1,"episodes":{
              "1":{"episode":1,"iframe":"https://player.test/s1e1","translation":{
                "66":{"translation":"Дублированный","quality":"BDRip","iframe":"https://player.test/s1e1?t=66"},
                "10":{"translation":"LostFilm","quality":"WEB-DL","iframe":"//player.test/s1e1?t=10"}}},
              "2":{"episode":2,"iframe":"https://player.test/s1e2","translation":{
                "66":{"translation":"Дублированный","iframe":"https://player.test/s1e2?t=66"}}}}},
            "2":{"season":2,"episodes":{
              "1":{"episode":1,"iframe":"https://player.test/s2e1","translation":{
                "10":{"translation":"LostFilm","iframe":"https://player.test/s2e1?t=10"}}}}}}}}
        """;

    private const string MovieJson = """
        {"status":"success","data":{"name":"Фильм","id_imdb":"tt0120689","iframe":"https://player.test/m"}}
        """;

    [Fact]
    public async Task Find_GroupsEpisodesByTranslationAcrossSeasons()
    {
        var series = await Create(new StubHandler(("alloha.test", SeriesJson))).FindAsync(BreakingBad, CancellationToken.None);

        Assert.Equal("Во все тяжкие", series!.Title.Name);
        Assert.Equal(2, series.Title.SeasonsCount);
        Assert.Equal([Rating.Imdb, Rating.Kinopoisk], series.Title.Ratings.Select(r => r.Source));

        var dub = Assert.Single(series.Streams, s => s.StreamId == "66");
        Assert.Equal("Дублированный", dub.Label);
        Assert.Equal([new SeriesEpisode(1, 1), new SeriesEpisode(1, 2)], dub.Episodes);

        var lostFilm = Assert.Single(series.Streams, s => s.StreamId == "10");
        Assert.Equal([new SeriesEpisode(1, 1), new SeriesEpisode(2, 1)], lostFilm.Episodes);
    }

    [Theory]
    [InlineData("10", 1, 1, "https://player.test/s1e1?t=10")]
    [InlineData("10", 2, 1, "https://player.test/s2e1?t=10")]
    [InlineData("66", 1, 2, "https://player.test/s1e2?t=66")]
    public async Task Playback_ReturnsEpisodeIframeForTranslation(string stream, int season, int episode, string expected)
    {
        var playback = await Create(new StubHandler(("alloha.test", SeriesJson)))
            .GetPlaybackAsync(BreakingBad, stream, season, episode, CancellationToken.None);

        Assert.Equal(PlaybackKind.Iframe, playback!.Kind);
        Assert.Equal(expected, playback.Url);
    }

    [Fact]
    public async Task Playback_NullWhenTranslationHasNoSuchEpisode()
    {
        var source = Create(new StubHandler(("alloha.test", SeriesJson)));

        Assert.Null(await source.GetPlaybackAsync(BreakingBad, "66", 2, 1, CancellationToken.None));
        Assert.Null(await source.GetPlaybackAsync(BreakingBad, "10", 9, 9, CancellationToken.None));
    }

    [Fact]
    public async Task MoviesAreNotSeries()
    {
        var source = Create(new StubHandler(("alloha.test", MovieJson)));

        Assert.Null(await source.FindAsync("tt0120689", CancellationToken.None));
        Assert.Empty(await source.SearchAsync("tt0120689", CancellationToken.None));
    }

    [Fact]
    public async Task Latest_UsesSerialListAndKeepsRecentSeries()
    {
        var handler = new StubHandler(("page=1", """
            {"status":"success","data":[
              {"name":"Новый","year":2026,"id_imdb":"tt1000001","seasons":{"1":{}}},
              {"name":"Старый","year":2008,"id_imdb":"tt0903747","seasons":{"1":{}}},
              {"name":"Фильм","year":2026,"id_imdb":"tt1000002"}]}
            """), ("page=2", """{"status":"success","data":[]}"""));

        var latest = await Create(handler).GetLatestAsync(24, CancellationToken.None);

        Assert.Equal("tt1000001", Assert.Single(latest).ImdbId);
        Assert.Contains("list=serial", handler.Requests[0].RequestUri!.Query);
    }

    private static AllohaSeriesSource Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new SeriesSourceDefinition { Id = "alloha-series", Type = "balancer", Url = "https://alloha.test", Token = "secret" }, new ManualTime());
}

public class SeriesConfigTests
{
    [Fact]
    public void Parse_ReadsSeriesSectionAndResolvesToken()
    {
        const string yaml = """
            series:
              sources:
                - id: alloha-series
                  adapter: alloha
                  type: balancer
                  token: ${ALLOHA_TOKEN}
                  latest: 12
            """;

        var config = SourcesConfigParser.Parse(yaml, name => name == "ALLOHA_TOKEN" ? "secret" : null);

        var source = Assert.Single(config.Series.Sources);
        Assert.Equal("secret", source.Token);
        Assert.Equal(12, source.Latest);
        Assert.Equal(SourceReadiness.Ready, SourceRegistry.GetReadiness(source));
    }

    [Fact]
    public void Readiness_RequiresAllohaToken()
    {
        var definition = new SeriesSourceDefinition { Id = "alloha", Type = "balancer" };

        Assert.Equal(SourceReadiness.NeedsToken, SourceRegistry.GetReadiness(definition));
    }

    [Fact]
    public void Parse_RejectsDuplicateIdAcrossSections()
    {
        const string yaml = """
            movies:
              sources:
                - id: alloha
                  type: balancer
            series:
              sources:
                - id: alloha
                  type: balancer
            """;

        var ex = Assert.Throws<SourcesConfigException>(() => SourcesConfigParser.Parse(yaml, _ => null));
        Assert.Contains(ex.Errors, e => e.Contains("повторяется", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RejectsLibraryTypeForSeries()
    {
        const string yaml = "series:\n  sources:\n    - id: x\n      type: library\n";

        Assert.Throws<SourcesConfigException>(() => SourcesConfigParser.Parse(yaml, _ => null));
    }
}

/// <summary>Пути и поля TMDB для сериалов взяты из документации: токена TMDB для проверки вживую не было.</summary>
public sealed class TmdbSeriesTests : IDisposable
{
    private readonly string _configPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_configPath);

    [Fact]
    public async Task Get_FindsTvByImdbIdAndMapsDetails()
    {
        var handler = new StubHandler(
            ("/3/find/tt0903747", """{"movie_results":[],"tv_results":[{"id":1396}]}"""),
            ("/3/tv/1396", """
                {"name":"Во все тяжкие","original_name":"Breaking Bad","first_air_date":"2008-01-20","overview":"Описание",
                 "poster_path":"/bb.jpg","number_of_seasons":5,"genres":[{"name":"Драма"}],"vote_average":8.9,"vote_count":15000}
                """));

        ISeriesCatalog catalog = Create(handler);
        var series = await catalog.GetAsync("tt0903747", CancellationToken.None);

        Assert.Equal("Во все тяжкие", series!.Name);
        Assert.Equal(2008, series.Year);
        Assert.Equal(5, series.SeasonsCount);
        Assert.Equal("https://img.test/w500/bb.jpg", series.PosterUrl);
        Assert.Equal([new Rating(Rating.Tmdb, 8.9)], series.Ratings);
    }

    [Fact]
    public async Task Browse_UsesDiscoverTvWithFirstAirDate()
    {
        var handler = new StubHandler(
            ("/3/discover/tv", """{"page":1,"total_pages":2,"results":[{"id":1396,"name":"Во все тяжкие","first_air_date":"2008-01-20","genre_ids":[18]}]}"""),
            ("/3/tv/1396/external_ids", """{"imdb_id":"tt0903747"}"""));

        var page = await Create(handler).BrowseSeriesAsync(new BrowseQuery(null, "18", 2005, null, BrowseSort.Date, 1), CancellationToken.None);

        var series = Assert.Single(page.Items);
        Assert.Equal("tt0903747", series.ImdbId);
        Assert.Equal(2008, series.Year);
        Assert.True(page.HasMore);

        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("sort_by=first_air_date.desc", url);
        Assert.Contains("first_air_date.gte=2005-01-01", url);
        Assert.Contains("with_genres=18", url);
    }

    [Fact]
    public async Task Genres_UseTvList()
    {
        var handler = new StubHandler(("/3/genre/tv/list", """{"genres":[{"id":18,"name":"драма"}]}"""));

        var genres = await Create(handler).GetSeriesGenresAsync(CancellationToken.None);

        Assert.Equal([new Genre("18", "Драма")], genres);
    }

    private TmdbCatalog Create(StubHandler handler)
    {
        File.WriteAllText(_configPath, """
            movies:
              catalog:
                url: https://tmdb.test/
                imagesUrl: https://img.test/w500/
                token: key
            """);
        var store = new SourcesConfigStore(_configPath, new ConfigurationBuilder().Build());
        return new TmdbCatalog(new HttpClient(handler), store, new MemoryCache(new MemoryCacheOptions()), new ManualTime());
    }
}
