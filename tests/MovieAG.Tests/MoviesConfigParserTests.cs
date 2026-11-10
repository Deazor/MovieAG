using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

public class MoviesConfigParserTests
{
    private const string Valid = """
        movies:
          catalog:
            token: ${TMDB_TOKEN}
          sources:
            - id: open
              adapter: library
              type: library
              items:
                - imdbId: tt1254207
                  name: Кролик
                  url: https://cdn/bunny.m3u8
        """;

    private static SourcesConfig Parse(string yaml, string? token = null) =>
        SourcesConfigParser.Parse(yaml, name => name == "TMDB_TOKEN" ? token : null);

    private static IReadOnlyList<string> Errors(string yaml) =>
        Assert.Throws<SourcesConfigException>(() => Parse(yaml)).Errors;

    [Fact]
    public void Parse_ReadsMoviesAndResolvesCatalogToken()
    {
        var config = Parse(Valid, "secret");

        Assert.Equal("secret", config.Movies.Catalog.Token);
        Assert.Equal("tt1254207", Assert.Single(Assert.Single(config.Movies.Sources).Items).ImdbId);
    }

    [Fact]
    public void Parse_ConfigWithoutMoviesSectionStaysValid()
    {
        var config = Parse("sources: []");

        Assert.Empty(config.Movies.Sources);
    }

    [Theory]
    [InlineData("tt1254207", "imdbId: 1254207", "imdbId")]
    [InlineData("name: Кролик", "name: ''", "name")]
    [InlineData("https://cdn/bunny.m3u8", "ftp://cdn/bunny.m3u8", "url")]
    public void Parse_RejectsInvalidItem(string from, string to, string field)
    {
        var yaml = from.StartsWith("tt", StringComparison.Ordinal)
            ? Valid.Replace("imdbId: " + from, to)
            : Valid.Replace(from, to);

        Assert.Contains(Errors(yaml), e => e.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_RejectsIdUsedInBothSections()
    {
        var yaml = "sources:\n  - id: open\n    type: api\n" + Valid;

        Assert.Contains(Errors(yaml), e => e.Contains("повторяется", StringComparison.Ordinal));
    }

    [Fact]
    public void Readiness_DoesNotAcceptAnimeAdapterForMovieSource()
    {
        var definition = new MovieSourceDefinition { Id = "x", Adapter = AniLibriaSource.AdapterName };

        Assert.Equal(SourceReadiness.NoAdapter, SourceRegistry.GetReadiness(definition));
    }

    [Fact]
    public void Readiness_AcceptsLibraryAdapter()
    {
        var definition = new MovieSourceDefinition { Id = "x", Adapter = MovieLibrarySource.AdapterName };

        Assert.Equal(SourceReadiness.Ready, SourceRegistry.GetReadiness(definition));
    }

    [Fact]
    public void Readiness_AcceptsKodikMovieAdapterWithoutToken()
    {
        var definition = new MovieSourceDefinition { Id = "kodik-movies", Type = "balancer", Adapter = KodikMovieSource.AdapterName };

        Assert.Equal(SourceReadiness.Ready, SourceRegistry.GetReadiness(definition));
    }

    [Fact]
    public void Parse_AcceptsBalancerMovieSource()
    {
        const string yaml = """
            movies:
              sources:
                - id: kodik-movies
                  adapter: kodik
                  type: balancer
                  token: ${KODIK_TOKEN}
            """;

        var config = SourcesConfigParser.Parse(yaml, _ => null);

        var source = Assert.Single(config.Movies.Sources);
        Assert.Equal(KodikMovieSource.AdapterName, source.AdapterName);
        Assert.Null(source.Token);
    }
}
