using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

public enum SourceReadiness { Ready, Disabled, NeedsToken, NoAdapter }

/// <summary>Строит адаптеры из конфига и пересобирает их при его изменении.</summary>
public sealed class SourceRegistry : ISourceProvider
{
    public const string HttpClientName = "sources";

    private static readonly Dictionary<string, (Func<HttpClient, SourceDefinition, IAnimeSource> Create, bool NeedsToken)> Adapters = new()
    {
        [AniLibriaSource.AdapterName] = ((http, def) => new AniLibriaSource(http, def), false),
        [KodikSource.AdapterName] = ((http, def) => new KodikSource(http, def), true)
    };

    private readonly SourcesConfigStore _store;
    private readonly IHttpClientFactory _httpClients;
    private volatile IReadOnlyList<IAnimeSource> _active = [];

    public SourceRegistry(SourcesConfigStore store, IHttpClientFactory httpClients)
    {
        _store = store;
        _httpClients = httpClients;
        store.Changed += Rebuild;
        Rebuild();
    }

    public IReadOnlyList<IAnimeSource> Active => _active;

    public static SourceReadiness GetReadiness(SourceDefinition definition)
    {
        if (!definition.Enabled) return SourceReadiness.Disabled;
        if (!Adapters.TryGetValue(definition.AdapterName, out var adapter)) return SourceReadiness.NoAdapter;
        return adapter.NeedsToken && string.IsNullOrEmpty(definition.Token)
            ? SourceReadiness.NeedsToken
            : SourceReadiness.Ready;
    }

    private void Rebuild()
    {
        _active = _store.Current.Sources
            .Where(s => GetReadiness(s) == SourceReadiness.Ready)
            .Select(s => Adapters[s.AdapterName].Create(_httpClients.CreateClient(HttpClientName), s))
            .ToList();
    }
}
