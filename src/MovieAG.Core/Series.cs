namespace MovieAG.Core;

/// <summary>Метаданные сериала; как и у фильмов, ключ во всех источниках и каталоге — IMDb ID.</summary>
public sealed record SeriesTitle
{
    public required string ImdbId { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public int? Year { get; init; }
    public int? SeasonsCount { get; init; }
    public string? Description { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<Rating> Ratings { get; init; } = [];

    /// <summary>Данные каталога важнее данных источника, но пустые поля каталога их не затирают.</summary>
    public SeriesTitle EnrichedBy(SeriesTitle? catalog) => catalog is null
        ? this
        : this with
        {
            Name = catalog.Name,
            OriginalName = catalog.OriginalName ?? OriginalName,
            Year = catalog.Year ?? Year,
            SeasonsCount = catalog.SeasonsCount ?? SeasonsCount,
            Description = catalog.Description ?? Description,
            PosterUrl = catalog.PosterUrl ?? PosterUrl,
            Genres = catalog.Genres.Count > 0 ? catalog.Genres : Genres,
            Ratings = Rating.Merge(catalog.Ratings, Ratings)
        };
}

public sealed record SeriesEpisode(int Season, int Episode);

/// <summary>Одна озвучка сериала у одного источника и серии, в которых она есть.</summary>
public sealed record SeriesStream(string SourceId, string StreamId, string Label, IReadOnlyList<SeriesEpisode> Episodes);

public sealed record SourceSeries(SeriesTitle Title, IReadOnlyList<SeriesStream> Streams);

public sealed record SeriesDetails(
    SeriesTitle Series,
    IReadOnlyList<SeriesStream> Streams,
    IReadOnlyList<string> FailedSources);

/// <summary>Адаптер источника сериалов. Сериалы адресуются IMDb ID, серии — номером сезона и серии.</summary>
public interface ISeriesSource : IMonitoredSource
{
    Task<IReadOnlyList<SeriesTitle>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<SeriesTitle>> GetLatestAsync(int limit, CancellationToken ct);

    Task<SourceSeries?> FindAsync(string imdbId, CancellationToken ct);

    Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, int season, int episode, CancellationToken ct);
}

/// <summary>Каталог метаданных сериалов; null означает, что данных нет или каталог не настроен.</summary>
public interface ISeriesCatalog
{
    Task<SeriesTitle?> GetAsync(string imdbId, CancellationToken ct);
}

/// <summary>Каталог сериалов с жанрами и постраничной выборкой.</summary>
public interface ISeriesBrowser
{
    Task<IReadOnlyList<Genre>> GetSeriesGenresAsync(CancellationToken ct);

    Task<BrowsePage<SeriesTitle>> BrowseSeriesAsync(BrowseQuery query, CancellationToken ct);
}

public interface ISeriesSourceProvider
{
    IReadOnlyList<ISeriesSource> Active { get; }
}
