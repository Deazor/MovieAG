using System.Text.RegularExpressions;

namespace MovieAG.Core;

public static partial class ImdbId
{
    public static bool IsValid(string? value) => value is not null && Format().IsMatch(value);

    [GeneratedRegex(@"^tt\d{7,10}$")]
    private static partial Regex Format();
}

/// <summary>Метаданные фильма; ключ во всех источниках и каталоге — IMDb ID.</summary>
public sealed record MovieTitle
{
    public required string ImdbId { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public int? Year { get; init; }
    public int? RuntimeMinutes { get; init; }
    public string? Description { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];

    /// <summary>Данные каталога важнее данных источника, но пустые поля каталога их не затирают.</summary>
    public MovieTitle EnrichedBy(MovieTitle? catalog) => catalog is null
        ? this
        : this with
        {
            Name = catalog.Name,
            OriginalName = catalog.OriginalName ?? OriginalName,
            Year = catalog.Year ?? Year,
            RuntimeMinutes = catalog.RuntimeMinutes ?? RuntimeMinutes,
            Description = catalog.Description ?? Description,
            PosterUrl = catalog.PosterUrl ?? PosterUrl,
            Genres = catalog.Genres.Count > 0 ? catalog.Genres : Genres
        };
}

/// <summary>Один вариант просмотра фильма у одного источника.</summary>
public sealed record MovieStream(string SourceId, string StreamId, string Label, string? License = null);

public sealed record SourceMovie(MovieTitle Title, IReadOnlyList<MovieStream> Streams);

public sealed record MovieCard(
    string ImdbId,
    string Name,
    string? OriginalName,
    int? Year,
    string? PosterUrl,
    IReadOnlyList<string> Sources);

public sealed record MovieListResult(
    IReadOnlyList<MovieCard> Items,
    IReadOnlyList<string> QueriedSources,
    IReadOnlyList<string> FailedSources);

public sealed record MovieDetails(
    MovieTitle Movie,
    IReadOnlyList<MovieStream> Streams,
    IReadOnlyList<string> FailedSources);

/// <summary>Адаптер источника фильмов. Фильмы адресуются IMDb ID.</summary>
public interface IMovieSource : IMonitoredSource
{
    Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct);

    Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct);

    Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct);

    Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct);
}

/// <summary>Каталог метаданных фильмов; null означает, что данных нет или каталог не настроен.</summary>
public interface IMovieCatalog
{
    Task<MovieTitle?> GetAsync(string imdbId, CancellationToken ct);
}

public interface IMovieSourceProvider
{
    IReadOnlyList<IMovieSource> Active { get; }
}
