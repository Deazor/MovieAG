using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

public enum SourceReadiness { Ready, Disabled, NeedsToken, NoAdapter }

/// <summary>Строит адаптеры из конфига и пересобирает их при его изменении.</summary>
public sealed class SourceRegistry : ISourceProvider, IMovieSourceProvider
{
    public const string HttpClientName = "sources";

    /// <summary>Зависимости, которые адаптеры получают при создании.</summary>
    private sealed record AdapterServices(HttpClient Http, IKodikTokenProvider KodikTokens);

    // Kodik работает и без токена в конфиге: тогда берётся публичный токен из скрипта плееров.
    private static readonly Dictionary<string, (Func<AdapterServices, SourceDefinition, IAnimeSource> Create, bool NeedsToken)> Adapters = new()
    {
        [AniLibriaSource.AdapterName] = ((s, def) => new AniLibriaSource(s.Http, def), false),
        [KodikSource.AdapterName] = ((s, def) => new KodikSource(s.Http, def, s.KodikTokens), false),
        [YummySource.AdapterName] = ((s, def) => new YummySource(s.Http, def), false)
    };

    private static readonly Dictionary<string, (Func<HttpClient, MovieSourceDefinition, IMovieSource> Create, bool NeedsToken)> MovieAdapters = new()
    {
        [MovieLibrarySource.AdapterName] = ((http, def) => new MovieLibrarySource(http, def), false)
    };

    private readonly SourcesConfigStore _store;
    private readonly IHttpClientFactory _httpClients;
    private readonly IKodikTokenProvider _kodikTokens;
    private volatile IReadOnlyList<IAnimeSource> _active = [];
    private volatile IReadOnlyList<IMovieSource> _activeMovies = [];

    public SourceRegistry(SourcesConfigStore store, IHttpClientFactory httpClients, IKodikTokenProvider kodikTokens)
    {
        _store = store;
        _httpClients = httpClients;
        _kodikTokens = kodikTokens;
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
            .Select(s => Adapters[s.AdapterName].Create(new AdapterServices(_httpClients.CreateClient(HttpClientName), _kodikTokens), s))
            .ToList();

        _activeMovies = config.Movies.Sources
            .Where(s => GetReadiness(s) == SourceReadiness.Ready)
            .Select(s => MovieAdapters[s.AdapterName].Create(_httpClients.CreateClient(HttpClientName), s))
            .ToList();
    }
}
