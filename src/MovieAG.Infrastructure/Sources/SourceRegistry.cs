using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

public enum SourceReadiness { Ready, Disabled, NeedsToken, NoAdapter }

/// <summary>Строит адаптеры из конфига и пересобирает их при его изменении.</summary>
public sealed class SourceRegistry : ISourceProvider, IMovieSourceProvider
{
    public const string HttpClientName = "sources";

    private static readonly Dictionary<string, (Func<HttpClient, SourceDefinition, IAnimeSource> Create, bool NeedsToken)> Adapters = new()
    {
        [AniLibriaSource.AdapterName] = ((http, def) => new AniLibriaSource(http, def), false),
        [KodikSource.AdapterName] = ((http, def) => new KodikSource(http, def), true),
        [YummySource.AdapterName] = ((http, def) => new YummySource(http, def), false)
    };

    private static readonly Dictionary<string, (Func<HttpClient, MovieSourceDefinition, IMovieSource> Create, bool NeedsToken)> MovieAdapters = new()
    {
        [MovieLibrarySource.AdapterName] = ((http, def) => new MovieLibrarySource(http, def), false)
    };

    private readonly SourcesConfigStore _store;
    private readonly IHttpClientFactory _httpClients;
    private volatile IReadOnlyList<IAnimeSource> _active = [];
    private volatile IReadOnlyList<IMovieSource> _activeMovies = [];

    public SourceRegistry(SourcesConfigStore store, IHttpClientFactory httpClients)
    {
        _store = store;
        _httpClients = httpClients;
        store.Changed += Rebuild;
        Rebuild();
    }

    public IReadOnlyList<IAnimeSource> Active => _active;

    IReadOnlyList<IMovieSource> IMovieSourceProvider.Active => _activeMovies;

    public static SourceReadiness GetReadiness(SourceDefinition definition)
    {
        if (!definition.Enabled) return SourceReadiness.Disabled;

        bool? needsToken = definition is MovieSourceDefinition
            ? MovieAdapters.TryGetValue(definition.AdapterName, out var movie) ? movie.NeedsToken : null
            : Adapters.TryGetValue(definition.AdapterName, out var anime) ? anime.NeedsToken : null;

        return needsToken switch
        {
            null => SourceReadiness.NoAdapter,
            true when string.IsNullOrEmpty(definition.Token) => SourceReadiness.NeedsToken,
            _ => SourceReadiness.Ready
        };
    }

    private void Rebuild()
    {
        var config = _store.Current;

        _active = config.Sources
            .Where(s => GetReadiness(s) == SourceReadiness.Ready)
            .Select(s => Adapters[s.AdapterName].Create(_httpClients.CreateClient(HttpClientName), s))
            .ToList();

        _activeMovies = config.Movies.Sources
            .Where(s => GetReadiness(s) == SourceReadiness.Ready)
            .Select(s => MovieAdapters[s.AdapterName].Create(_httpClients.CreateClient(HttpClientName), s))
            .ToList();
    }
}
