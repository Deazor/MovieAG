using System.Globalization;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;

namespace MovieAG.Infrastructure.Sources;

/// <summary>Фильмы, перечисленные в конфиге: открытые или принадлежащие пользователю HLS-потоки.</summary>
public sealed class MovieLibrarySource(HttpClient http, MovieSourceDefinition definition) : IMovieSource
{
    public const string AdapterName = "library";

    private readonly ILookup<string, LibraryMovie> _movies = definition.Items.ToLookup(m => m.ImdbId, StringComparer.Ordinal);

    public string Id => definition.Id;

    public Task<IReadOnlyList<MovieTitle>> SearchAsync(string query, CancellationToken ct)
    {
        IReadOnlyList<MovieTitle> found = _movies
            .Where(group => group.Any(movie => Matches(movie, query)))
            .Select(group => ToTitle(group.First()))
            .ToList();
        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<MovieTitle>> GetLatestAsync(int limit, CancellationToken ct)
    {
        IReadOnlyList<MovieTitle> latest = _movies.Take(limit).Select(group => ToTitle(group.First())).ToList();
        return Task.FromResult(latest);
    }

    public Task<SourceMovie?> FindAsync(string imdbId, CancellationToken ct)
    {
        var variants = _movies[imdbId].ToList();
        if (variants.Count == 0) return Task.FromResult<SourceMovie?>(null);

        var streams = variants
            .Select((movie, index) => new MovieStream(Id, ToStreamId(index), movie.Label, movie.License))
            .ToList();
        return Task.FromResult<SourceMovie?>(new SourceMovie(ToTitle(variants[0]), streams));
    }

    public Task<Playback?> GetPlaybackAsync(string imdbId, string streamId, CancellationToken ct)
    {
        var variant = _movies[imdbId].Select((movie, index) => (movie, index))
            .FirstOrDefault(v => ToStreamId(v.index) == streamId).movie;

        return Task.FromResult(variant is null ? null : new Playback(PlaybackKind.Hls, variant.Url, []));
    }

    public async Task PingAsync(CancellationToken ct)
    {
        var first = definition.Items.FirstOrDefault();
        if (first is null) return;

        using var response = await http.GetAsync(first.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string ToStreamId(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static bool Matches(LibraryMovie movie, string query) =>
        movie.ImdbId.Equals(query, StringComparison.OrdinalIgnoreCase)
        || movie.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (movie.OriginalName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static MovieTitle ToTitle(LibraryMovie movie) => new()
    {
        ImdbId = movie.ImdbId,
        Name = movie.Name,
        OriginalName = movie.OriginalName,
        Year = movie.Year,
        Description = movie.Description,
        PosterUrl = movie.PosterUrl,
        Genres = movie.Genres
    };
}
