namespace MovieAG.Core;

/// <summary>Адаптер источника видео: балансера, API или сайта.</summary>
public interface IAnimeSource
{
    string Id { get; }

    Task<IReadOnlyList<SourceTitle>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<SourceTitle>> GetLatestAsync(int limit, CancellationToken ct);

    Task<IReadOnlyList<DubVariant>> GetVariantsAsync(CatalogTitle title, CancellationToken ct);

    Task<Playback?> GetPlaybackAsync(CatalogTitle title, string dubId, int episode, CancellationToken ct);

    Task PingAsync(CancellationToken ct);
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
