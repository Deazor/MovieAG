using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using MovieAG.Infrastructure.Catalog;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Tests;

public sealed class TmdbCatalogTests : IDisposable
{
    private const string Id = "tt1254207";
    private const string Find = """{ "movie_results": [ { "id": 10378 } ] }""";
    private const string Details = """
        {
          "title": "Большой кролик",
          "original_title": "Big Buck Bunny",
          "overview": "Описание",
          "poster_path": "/poster.jpg",
          "release_date": "2008-04-10",
          "runtime": 10,
          "genres": [ { "name": "мультфильм" } ]
        }
        """;

    private readonly string _configPath = Path.GetTempFileName();

    public void Dispose() => File.Delete(_configPath);

    private TmdbCatalog Create(StubHandler handler, string? token)
    {
        var tokenLine = token is null ? "" : $"    token: {token}";
        File.WriteAllText(_configPath, $"""
            movies:
              catalog:
                url: https://tmdb.test/
                imagesUrl: https://img.test/w500/
            {tokenLine}
            """);

        var store = new SourcesConfigStore(_configPath, new ConfigurationBuilder().Build());
        return new TmdbCatalog(new HttpClient(handler), store, new MemoryCache(new MemoryCacheOptions()));
    }

    private static StubHandler Stub() => new(("/3/find/" + Id, Find), ("/3/movie/10378", Details));

    [Fact]
    public async Task Get_ResolvesImdbIdAndMapsDetails()
    {
        var title = await Create(Stub(), "key").GetAsync(Id, CancellationToken.None);

        Assert.Equal(Id, title!.ImdbId);
        Assert.Equal("Большой кролик", title.Name);
        Assert.Equal("Big Buck Bunny", title.OriginalName);
        Assert.Equal(2008, title.Year);
        Assert.Equal(10, title.RuntimeMinutes);
        Assert.Equal("https://img.test/w500/poster.jpg", title.PosterUrl);
        Assert.Equal(["мультфильм"], title.Genres);
    }

    [Fact]
    public async Task Get_RequestsRussianLocaleAndSendsShortKeyInQuery()
    {
        var handler = Stub();

        await Create(handler, "short-key").GetAsync(Id, CancellationToken.None);

        var find = handler.Requests[0].RequestUri!.ToString();
        Assert.Contains("external_source=imdb_id", find);
        Assert.Contains("language=ru-RU", find);
        Assert.Contains("api_key=short-key", find);
        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Get_SendsReadTokenAsBearer()
    {
        var handler = Stub();

        await Create(handler, "aaa.bbb.ccc").GetAsync(Id, CancellationToken.None);

        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
        Assert.DoesNotContain("api_key", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Get_MakesNoRequestsWithoutToken()
    {
        var handler = Stub();

        Assert.Null(await Create(handler, null).GetAsync(Id, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Get_CachesResult()
    {
        var handler = Stub();
        var catalog = Create(handler, "key");

        await catalog.GetAsync(Id, CancellationToken.None);
        var requests = handler.Requests.Count;
        await catalog.GetAsync(Id, CancellationToken.None);

        Assert.Equal(requests, handler.Requests.Count);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenMovieIsUnknown()
    {
        var handler = new StubHandler(("/3/find/", """{ "movie_results": [] }"""));

        Assert.Null(await Create(handler, "key").GetAsync(Id, CancellationToken.None));
    }

    [Fact]
    public async Task Get_ThrowsOnRejectedToken()
    {
        var handler = new StubHandler { ForcedStatus = HttpStatusCode.Unauthorized };

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(handler, "key").GetAsync(Id, CancellationToken.None));
    }
}
