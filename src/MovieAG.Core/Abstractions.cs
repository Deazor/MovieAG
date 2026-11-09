namespace MovieAG.Core;

/// <summary>Источник, доступность которого показывается на экране «Источники».</summary>
public interface IMonitoredSource
{
    string Id { get; }

    Task PingAsync(CancellationToken ct);
}

/// <summary>Адаптер источника видео: балансера, API или сайта.</summary>
public interface IAnimeSource : IMonitoredSource
{
    Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct);

    Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct);
}

public interface ICatalog
{
    Task<IReadOnlyList<CatalogTitle>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<CatalogTitle>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct);

    Task<CatalogTitle?> GetAsync(int id, CancellationToken ct);
}

/// <summary>Источники, включённые в конфиге и готовые к работе, в порядке приоритета.</summary>
public interface ISourceProvider
{
    IReadOnlyList<IAnimeSource> Active { get; }
}
