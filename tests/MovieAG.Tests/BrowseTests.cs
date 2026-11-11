using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MovieAG.Core;
using MovieAG.Infrastructure.Catalog;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Tests;

/// <summary>
/// Параметры Shikimori (genre, order, status=!anons, season, page) сверены с живым API 2026-11-10.
/// Ответы TMDB составлены по документации: токена TMDB для проверки вживую не было.
/// </summary>
public sealed class BrowseTests : IDisposable
{
    private const int CurrentYear = 2026;
    private readonly string _configPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_configPath);

    [Theory]
    [InlineData(null, null, null, null, null, true)]
    [InlineData("22", 2015, 2020, "rating", 3, true)]
    [InlineData("22abc", null, null, null, null, false)]
    [InlineData(null, 2020, 2015, null, null, false)]
    [InlineData(null, 1800, null, null, null, false)]
    [InlineData(null, null, 2040, null, null, false)]
    [InlineData(null, null, null, "random", null, false)]
    [InlineData(null, null, null, null, 0, false)]
    [InlineData(null, null, null, null, 501, false)]
    public void TryCreate_ValidatesParameters(string? genre, int? from, int? to, string? sort, int? page, bool isValid)
    {
        var query = BrowseQuery.TryCreate(null, genre, from, to, sort, page, CurrentYear, out var error);

        Assert.Equal(isValid, query is not null);
        Assert.Equal(isValid, error is null);
    }

    [Fact]
    public void TryCreate_DefaultsToNewestFirstAndFirstPage()
    {
        var query = BrowseQuery.TryCreate("  ангел  ", "", null, null, null, null, CurrentYear, out _)!;

        Assert.Equal("ангел", query.Text);
        Assert.Null(query.Genre);
        Assert.Equal(BrowseSort.Date, query.Sort);
        Assert.Equal(1, query.Page);
    }

    [Fact]
    public async Task Shikimori_BrowseSendsGenreSeasonOrderAndPage()
    {
        var handler = new StubHandler(("/api/animes", "[]"));
        var query = new BrowseQuery("ангел", "22", 2015, 2020, BrowseSort.Date, 2);

        var page = await CreateShikimori(handler).BrowseAsync(query, CancellationToken.None);

        var url = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("genre=22", url);
        Assert.Contains("season=2015_2020", url);
        Assert.Contains("order=aired_on", url);
        Assert.Contains("status=!anons", url);
        Assert.Contains("page=2", url);
        Assert.Contains("search=ангел", url);
        Assert.False(page.HasMore);
    }

    [Theory]
    [InlineData(2019, 2019, "season=2019")]
    [InlineData(2010, null, "season=2010_2027")]
    [InlineData(null, 2000, "season=1917_2000")]
    public async Task Shikimori_YearRangeBecomesSeason(int? from, int? to, string expected)
    {
        var handler = new StubHandler(("/api/animes", "[]"));

        await CreateShikimori(handler).BrowseAsync(new BrowseQuery(null, null, from, to, BrowseSort.Popularity, 1), CancellationToken.None);

        var url = handler.Requests[0].RequestUri!.Query;
        Assert.Contains(expected, url);
        Assert.Contains("order=popularity", url);
        Assert.DoesNotContain("status=", url);
    }

    [Fact]
    public async Task Shikimori_FullPageMeansMore()
    {
        var items = string.Join(',', Enumerable.Range(1, ShikimoriCatalog.BrowsePageSize).Select(i => $$"""{"id":{{i}},"name":"A{{i}}"}"""));
        var handler = new StubHandler(("/api/animes", $"[{items}]"));

        var page = await CreateShikimori(handler).BrowseAsync(new BrowseQuery(null, "22", null, null, BrowseSort.Date, 1), CancellationToken.None);

        Assert.Equal(ShikimoriCatalog.BrowsePageSize, page.Items.Count);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Shikimori_GenresKeepOnlyAnimeSortedByRussianName()
    {
        var handler = new StubHandler(("/api/genres", """
            [{"id":22,"name":"Romance","russian":"Романтика","kind":"genre","entry_type":"Anime"},
             {"id":4,"name":"Comedy","russian":"Комедия","kind":"genre","entry_type":"Anime"},
             {"id":99,"name":"Romance","russian":"Романтика","kind":"genre","entry_type":"Manga"}]
            """));

        var genres = await CreateShikimori(handler).GetGenresAsync(CancellationToken.None);

        Assert.Equal([new Genre("4", "Комедия"), new Genre("22", "Романтика")], genres);
    }

    [Fact]
    public async Task Tmdb_DiscoverMapsImdbIdsAndSkipsMoviesWithoutThem()
    {
        var handler = new StubHandler(
            ("/3/discover/movie", """
                {"page":1,"total_pages":3,"results":[
                  {"id":1,"title":"Дневник памяти","original_title":"The Notebook","release_date":"2004-06-25","poster_path":"/n.jpg","genre_ids":[10749],"vote_average":7.88,"vote_count":12000},
                  {"id":2,"title":"Без IMDb","release_date":"2024-01-01","genre_ids":[10749],"vote_average":9.5,"vote_count":3}]}
                """),
            ("/3/movie/1/external_ids", """{"imdb_id":"tt0332280"}"""),
            ("/3/movie/2/external_ids", """{"imdb_id":null}"""));

        var page = await CreateTmdb(handler, "key").BrowseAsync(new BrowseQuery(null, "10749", 2000, null, BrowseSort.Date, 1), CancellationToken.None);

        var movie = Assert.Single(page.Items);
        Assert.Equal("tt0332280", movie.ImdbId);
        Assert.Equal(2004, movie.Year);
        Assert.Equal("https://img.test/w500/n.jpg", movie.PosterUrl);
        Assert.Equal([new Rating(Rating.Tmdb, 7.9)], movie.Ratings);
        Assert.True(page.HasMore);

        var discover = Uri.UnescapeDataString(handler.Requests[0].RequestUri!.Query);
        Assert.Contains("with_genres=10749", discover);
        Assert.Contains("sort_by=primary_release_date.desc", discover);
        Assert.Contains("primary_release_date.gte=2000-01-01", discover);
        Assert.Contains("primary_release_date.lte=2026-01-01", discover);
    }

    [Fact]
    public async Task Tmdb_SearchFiltersGenreAndYearWithinPage()
    {
        var handler = new StubHandler(
            ("/3/search/movie", """
                {"page":1,"total_pages":1,"results":[
                  {"id":1,"title":"Подходит","release_date":"2010-01-01","genre_ids":[10749]},
                  {"id":2,"title":"Другой жанр","release_date":"2010-01-01","genre_ids":[28]},
                  {"id":3,"title":"Старый","release_date":"1990-01-01","genre_ids":[10749]}]}
                """),
            ("/3/movie/1/external_ids", """{"imdb_id":"tt1000001"}"""),
            ("/3/movie/2/external_ids", """{"imdb_id":"tt1000002"}"""),
            ("/3/movie/3/external_ids", """{"imdb_id":"tt1000003"}"""));

        var page = await CreateTmdb(handler, "key").BrowseAsync(new BrowseQuery("любовь", "10749", 2000, null, BrowseSort.Date, 1), CancellationToken.None);

        Assert.Equal("tt1000001", Assert.Single(page.Items).ImdbId);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task Tmdb_RatingSortRequiresVotes()
    {
        var handler = new StubHandler(("/3/discover/movie", """{"page":1,"total_pages":1,"results":[]}"""));

        await CreateTmdb(handler, "key").BrowseAsync(new BrowseQuery(null, null, null, null, BrowseSort.Rating, 1), CancellationToken.None);

        var url = handler.Requests[0].RequestUri!.Query;
        Assert.Contains("sort_by=vote_average.desc", url);
        Assert.Contains("vote_count.gte=200", url);
        Assert.DoesNotContain("primary_release_date.lte", url);
    }

    [Fact]
    public async Task Tmdb_GenresAreCapitalized()
    {
        var handler = new StubHandler(("/3/genre/movie/list", """{"genres":[{"id":10749,"name":"мелодрама"},{"id":28,"name":"боевик"}]}"""));

        var genres = await CreateTmdb(handler, "key").GetGenresAsync(CancellationToken.None);

        Assert.Equal([new Genre("28", "Боевик"), new Genre("10749", "Мелодрама")], genres);
    }

    [Fact]
    public async Task Tmdb_WithoutTokenThrowsUnavailable()
    {
        var handler = new StubHandler();

        await Assert.ThrowsAsync<CatalogUnavailableException>(() =>
            CreateTmdb(handler, null).BrowseAsync(new BrowseQuery(null, null, null, null, BrowseSort.Date, 1), CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    private ShikimoriCatalog CreateShikimori(StubHandler handler) =>
        new(new HttpClient(handler), CreateStore(null), new ManualTime());

    private TmdbCatalog CreateTmdb(StubHandler handler, string? token) =>
        new(new HttpClient(handler), CreateStore(token), new MemoryCache(new MemoryCacheOptions()), new ManualTime());

    private SourcesConfigStore CreateStore(string? tmdbToken)
    {
        var tokenLine = tmdbToken is null ? "" : $"    token: {tmdbToken}";
        File.WriteAllText(_configPath, $"""
            catalog:
              provider: shikimori
              url: https://shiki.test
            movies:
              catalog:
                url: https://tmdb.test/
                imagesUrl: https://img.test/w500/
            {tokenLine}
            """);
        return new SourcesConfigStore(_configPath, new ConfigurationBuilder().Build());
    }
}

/// <summary>Форма ответов REST и GraphQL Shikimori снята с живого API 2026-11-11 (Mushoku Tensei III).</summary>
public sealed class ShikimoriPosterAndScoreTests : IDisposable
{
    private readonly string _configPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_configPath);

    private const string ListJson = """
        [{"id":59193,"russian":"Реинкарнация безработного 3","score":"8.66","image":{"original":"/assets/globals/missing_original.jpg"}},
         {"id":14407,"russian":"Персона 3","score":"7.52","image":{"original":"/system/animes/original/14407.jpg?1"}},
         {"id":65077,"russian":"Анонс","score":"0.0","image":{"original":"/assets/globals/missing_original.jpg"}}]
        """;

    private const string GraphQlJson = """
        {"data":{"animes":[
          {"id":"59193","poster":{"originalUrl":"https://shiki.test/uploads/poster/animes/59193/a.jpeg","mainUrl":"https://shiki.test/uploads/poster/animes/59193/main-a.webp"}},
          {"id":"65077","poster":null}]}}
        """;

    [Fact]
    public async Task MissingPostersAreFilledFromGraphQlAndScoreParsed()
    {
        var handler = new StubHandler(("/api/animes", ListJson), ("/api/graphql", GraphQlJson));

        var titles = await Create(handler).SearchAsync("mushoku", CancellationToken.None);

        Assert.Equal("https://shiki.test/uploads/poster/animes/59193/a.jpeg", titles[0].PosterUrl);
        Assert.Equal("https://shiki.test/system/animes/original/14407.jpg?1", titles[1].PosterUrl);
        Assert.Null(titles[2].PosterUrl);
        Assert.Equal([8.66, 7.52], titles.Take(2).Select(t => t.Score!.Value));
        Assert.Null(titles[2].Score);

        var graphQl = Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath == "/api/graphql");
        Assert.Equal(HttpMethod.Post, graphQl.Method);
        var body = await graphQl.Content!.ReadAsStringAsync();
        Assert.Contains("59193,65077", body);
        Assert.DoesNotContain("14407", body);
    }

    [Fact]
    public async Task NoGraphQlRequestWhenAllPostersPresent()
    {
        var handler = new StubHandler(("/api/animes", """[{"id":1,"name":"A","image":{"original":"/system/animes/original/1.jpg"}}]"""));

        await Create(handler).SearchAsync("a", CancellationToken.None);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GraphQlFailureKeepsTitlesWithoutPoster()
    {
        var handler = new StubHandler(("/api/animes", ListJson));

        var titles = await Create(handler).SearchAsync("mushoku", CancellationToken.None);

        Assert.Equal(3, titles.Count);
        Assert.Null(titles[0].PosterUrl);
    }

    private ShikimoriCatalog Create(StubHandler handler)
    {
        File.WriteAllText(_configPath, "catalog:\n  provider: shikimori\n  url: https://shiki.test\n");
        return new ShikimoriCatalog(new HttpClient(handler), new SourcesConfigStore(_configPath, new ConfigurationBuilder().Build()), new ManualTime());
    }
}

public class RatingTests
{
    [Theory]
    [InlineData(8.66, 8.7)]
    [InlineData(10.0, 10.0)]
    [InlineData(0.0, null)]
    [InlineData(null, null)]
    [InlineData(11.0, null)]
    public void Create_RoundsAndRejectsEmpty(double? value, double? expected)
    {
        Assert.Equal(expected, Rating.Create(Rating.Imdb, value)?.Value);
    }

    [Fact]
    public void Merge_KeepsFirstPerSourceInFixedOrder()
    {
        var merged = Rating.Merge(
            [new Rating(Rating.Tmdb, 7.9), new Rating(Rating.Imdb, 8.1)],
            [new Rating(Rating.Imdb, 9.9), new Rating(Rating.Kinopoisk, 8.4)]);

        Assert.Equal([new Rating(Rating.Imdb, 8.1), new Rating(Rating.Kinopoisk, 8.4), new Rating(Rating.Tmdb, 7.9)], merged);
    }

    [Fact]
    public void EnrichedBy_AddsCatalogRatingToSourceRatings()
    {
        var source = new MovieTitle { ImdbId = "tt1", Name = "A", Ratings = [new Rating(Rating.Imdb, 8.6)] };
        var catalog = new MovieTitle { ImdbId = "tt1", Name = "Б", Ratings = [new Rating(Rating.Tmdb, 8.5)] };

        Assert.Equal([Rating.Imdb, Rating.Tmdb], source.EnrichedBy(catalog).Ratings.Select(r => r.Source));
    }
}
