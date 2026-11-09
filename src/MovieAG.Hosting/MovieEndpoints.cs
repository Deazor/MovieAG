using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieAG.Core;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Hosting;

public sealed record SaveMovieProgressRequest(string SourceId, string StreamId, int Seconds, int? DurationSeconds);

public sealed record MovieProgressItem(
    string ImdbId,
    string Name,
    string? PosterUrl,
    string SourceId,
    string StreamId,
    int PositionSeconds,
    int? DurationSeconds);

internal static class MovieEndpoints
{
    private const string MovieRoute = "/{imdbId:regex(^tt\\d+$)}";
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 200;
    private const int LatestLimit = 24;
    private const int HistoryLimit = 6;
    private const int MaxPositionSeconds = 24 * 60 * 60;

    public static void MapMovies(this RouteGroupBuilder api)
    {
        var movies = api.MapGroup("/movies");

        movies.MapGet("/", List);
        movies.MapGet("/history", GetHistory);
        movies.MapGet(MovieRoute, GetMovie);
        movies.MapGet(MovieRoute + "/playback", GetPlayback);
        movies.MapGet(MovieRoute + "/progress", GetProgress);
        movies.MapPut(MovieRoute + "/progress", SaveProgress);
    }

    private static async Task<IResult> List(string? q, MovieService service, CancellationToken ct)
    {
        var query = q?.Trim() ?? "";
        if (query.Length == 0) return Results.Ok(await service.GetLatestAsync(LatestLimit, ct));

        if (query.Length is < MinQueryLength or > MaxQueryLength)
        {
            return Results.Problem(
                $"Запрос должен содержать от {MinQueryLength} до {MaxQueryLength} символов",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(await service.SearchAsync(query, ct));
    }

    private static async Task<IResult> GetMovie(string imdbId, MovieService service, CancellationToken ct)
    {
        var details = await service.GetAsync(imdbId, ct);
        return details is null ? Results.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetPlayback(
        string imdbId,
        string source,
        string stream,
        MovieService service,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        try
        {
            var playback = await service.GetPlaybackAsync(imdbId, source, stream, ct);
            return playback is null ? Results.NotFound() : Results.Ok(playback);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            loggers.CreateLogger("MoviePlayback").LogWarning(ex, "Источник {Source} не отдал поток", source);
            return Results.Problem($"Источник {source} не отвечает", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IReadOnlyList<MovieProgressItem>> GetHistory(AppDbContext db, CancellationToken ct)
    {
        var items = await db.MovieProgress
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        return items.Select(ToItem).ToList();
    }

    private static async Task<IResult> GetProgress(string imdbId, AppDbContext db, CancellationToken ct)
    {
        var progress = await db.MovieProgress.AsNoTracking().FirstOrDefaultAsync(p => p.ImdbId == imdbId, ct);
        return progress is null ? Results.NotFound() : Results.Ok(ToItem(progress));
    }

    private static async Task<IResult> SaveProgress(
        string imdbId,
        SaveMovieProgressRequest request,
        AppDbContext db,
        MovieService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceId)
            || string.IsNullOrWhiteSpace(request.StreamId)
            || request.Seconds is < 0 or > MaxPositionSeconds
            || request.DurationSeconds is < 0 or > MaxPositionSeconds)
        {
            return Results.Problem("Некорректные данные прогресса", statusCode: StatusCodes.Status400BadRequest);
        }

        var progress = await db.MovieProgress.FirstOrDefaultAsync(p => p.ImdbId == imdbId, ct);
        if (progress is null)
        {
            var details = await service.GetAsync(imdbId, ct);
            if (details is null) return Results.NotFound();

            progress = new MovieProgress
            {
                ImdbId = imdbId,
                Name = details.Movie.Name,
                PosterUrl = details.Movie.PosterUrl
            };
            db.MovieProgress.Add(progress);
        }

        progress.SourceId = request.SourceId;
        progress.StreamId = request.StreamId;
        progress.PositionSeconds = request.Seconds;
        progress.DurationSeconds = request.DurationSeconds ?? progress.DurationSeconds;
        progress.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static MovieProgressItem ToItem(MovieProgress progress) => new(
        progress.ImdbId,
        progress.Name,
        progress.PosterUrl,
        progress.SourceId,
        progress.StreamId,
        progress.PositionSeconds,
        progress.DurationSeconds);
}
