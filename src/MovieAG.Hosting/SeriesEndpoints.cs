using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieAG.Core;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Hosting;

public sealed record SaveSeriesProgressRequest(
    string SourceId,
    string StreamId,
    string StreamLabel,
    int Season,
    int Episode,
    int Seconds,
    int? DurationSeconds);

public sealed record SeriesProgressItem(
    string ImdbId,
    string Name,
    string? PosterUrl,
    string SourceId,
    string StreamId,
    string StreamLabel,
    int Season,
    int Episode,
    int PositionSeconds,
    int? DurationSeconds);

/// <summary>Раздел «Сериалы»: новинки и поиск по источникам, карточка с сериями, плеер и история просмотра.</summary>
internal static class SeriesEndpoints
{
    private const string SeriesRoute = "/{imdbId:regex(^tt\\d+$)}";
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 200;
    private const int LatestLimit = 24;
    private const int HistoryLimit = 6;
    private const int MaxNumber = 10_000;
    private const int MaxPositionSeconds = 24 * 60 * 60;
    private const int MaxLabelLength = 200;

    public static void MapSeries(this RouteGroupBuilder api)
    {
        var series = api.MapGroup("/series");

        series.MapGet("/", List);
        series.MapGet("/history", GetHistory);
        series.MapGet(SeriesRoute, GetSeries);
        series.MapGet(SeriesRoute + "/playback", GetPlayback);
        series.MapGet(SeriesRoute + "/progress", GetProgress);
        series.MapPut(SeriesRoute + "/progress", SaveProgress);
    }

    private static async Task<IResult> List(string? q, SeriesService service, CancellationToken ct)
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

    private static async Task<IResult> GetSeries(string imdbId, SeriesService service, CancellationToken ct)
    {
        var details = await service.GetAsync(imdbId, ct);
        return details is null ? Results.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetPlayback(
        string imdbId,
        string source,
        string stream,
        int season,
        int episode,
        SeriesService service,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (season is < 0 or > MaxNumber || episode is < 0 or > MaxNumber)
        {
            return Results.Problem("Некорректный номер сезона или серии", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var playback = await service.GetPlaybackAsync(imdbId, source, stream, season, episode, ct);
            return playback is null ? Results.NotFound() : Results.Ok(playback);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            loggers.CreateLogger("SeriesPlayback").LogWarning(ex, "Источник {Source} не отдал серию", source);
            return Results.Problem($"Источник {source} не отвечает", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IReadOnlyList<SeriesProgressItem>> GetHistory(AppDbContext db, CancellationToken ct)
    {
        var items = await db.SeriesProgress
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(HistoryLimit)
            .ToListAsync(ct);

        return items.Select(ToItem).ToList();
    }

    private static async Task<IResult> GetProgress(string imdbId, AppDbContext db, CancellationToken ct)
    {
        var progress = await db.SeriesProgress.AsNoTracking().FirstOrDefaultAsync(p => p.ImdbId == imdbId, ct);
        return progress is null ? Results.NotFound() : Results.Ok(ToItem(progress));
    }

    /// <summary>Одна запись на сериал: при переходе к другой серии она перезаписывается.</summary>
    private static async Task<IResult> SaveProgress(
        string imdbId,
        SaveSeriesProgressRequest request,
        AppDbContext db,
        SeriesService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SourceId)
            || string.IsNullOrWhiteSpace(request.StreamId)
            || request.StreamLabel is null || request.StreamLabel.Length > MaxLabelLength
            || request.Season is < 0 or > MaxNumber
            || request.Episode is < 0 or > MaxNumber
            || request.Seconds is < 0 or > MaxPositionSeconds
            || request.DurationSeconds is < 0 or > MaxPositionSeconds)
        {
            return Results.Problem("Некорректные данные прогресса", statusCode: StatusCodes.Status400BadRequest);
        }

        var progress = await db.SeriesProgress.FirstOrDefaultAsync(p => p.ImdbId == imdbId, ct);
        if (progress is null)
        {
            var details = await service.GetAsync(imdbId, ct);
            if (details is null) return Results.NotFound();

            progress = new SeriesProgress
            {
                ImdbId = imdbId,
                Name = details.Series.Name,
                PosterUrl = details.Series.PosterUrl
            };
            db.SeriesProgress.Add(progress);
        }

        progress.SourceId = request.SourceId;
        progress.StreamId = request.StreamId;
        progress.StreamLabel = request.StreamLabel;
        progress.Season = request.Season;
        progress.Episode = request.Episode;
        progress.PositionSeconds = request.Seconds;
        progress.DurationSeconds = request.DurationSeconds;
        progress.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static SeriesProgressItem ToItem(SeriesProgress progress) => new(
        progress.ImdbId,
        progress.Name,
        progress.PosterUrl,
        progress.SourceId,
        progress.StreamId,
        progress.StreamLabel,
        progress.Season,
        progress.Episode,
        progress.PositionSeconds,
        progress.DurationSeconds);
}
