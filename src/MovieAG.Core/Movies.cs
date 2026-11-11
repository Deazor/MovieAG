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
    public IReadOnlyList<Rating> Ratings { get; init; } = [];

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
            Genres = catalog.Genres.Count > 0 ? catalog.Genres : Genres,
            Ratings = Rating.Merge(catalog.Ratings, Ratings)
        };
}

/// <summary>Оценка фильма по десятибалльной шкале с названием сервиса: IMDb, КП, TMDB.</summary>
public sealed record Rating(string Source, double Value)
{
    public const string Imdb = "IMDb";
    public const string Kinopoisk = "КП";
    public const string Tmdb = "TMDB";

    private static readonly string[] Order = [Imdb, Kinopoisk, Tmdb];

    /// <summary>Положительную оценку округляет до десятых; ноль у сервисов означает «оценок нет».</summary>
    public static Rating? Create(string source, double? value) =>
        value is > 0 and <= 10 ? new Rating(source, Math.Round(value.Value, 1)) : null;

    /// <summary>По одной оценке на сервис; при совпадении побеждает первый список. Порядок: IMDb, КП, TMDB.</summary>
    public static IReadOnlyList<Rating> Merge(params IEnumerable<Rating>[] lists) =>
        lists.SelectMany(l => l)
            .DistinctBy(r => r.Source)
            .OrderBy(r => Array.IndexOf(Order, r.Source) is var i and >= 0 ? i : Order.Length)
            .ToList();
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
    IReadOnlyList<string> Sources,
    IReadOnlyList<Rating> Ratings);

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
