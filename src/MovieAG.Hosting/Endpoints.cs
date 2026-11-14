using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Persistence;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Hosting;

public sealed record SaveProgressRequest(int Episode, string DubKey, string DubName, string SourceId);

public sealed record SavePositionRequest(int Episode, int Seconds, int? DurationSeconds);

public sealed record SetEnabledRequest(bool Enabled);

public sealed record ConfigDto(string Yaml);

public sealed record HistoryItem(
    int TitleId,
    string Name,
    string? PosterUrl,
    int LastEpisode,
    int? EpisodesTotal,
    int WatchedCount,
    string DubKey,
    string DubName,
    string SourceId,
    IReadOnlyList<int> WatchedEpisodes,
    int PositionSeconds,
    int? DurationSeconds);

public static class Endpoints
{
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 200;
    private const int FreshLimit = 12;
    private const int HistoryLimit = 6;
    private const int MaxPositionSeconds = 24 * 60 * 60;

    public static void MapMovieAgApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/search", Search);
        api.MapGet("/fresh", (AggregationService service, CancellationToken ct) => service.GetFreshAsync(FreshLimit, ct));
        api.MapGet("/titles/{id:int}", GetTitle);
        api.MapGet("/titles/{id:int}/playback", GetPlayback);

        api.MapGet("/history", GetHistory);
        api.MapGet("/history/{id:int}", GetProgress);
        api.MapPut("/history/{id:int}", SaveProgress);
        api.MapPut("/history/{id:int}/position", SavePosition);

        api.MapGet("/sources", (SourceHealthService health) => health.GetStatuses());
        api.MapPost("/sources/check", CheckSources);
        api.MapPut("/sources/{id}/enabled", SetEnabled);
        api.MapGet("/sources/config", async (SourcesConfigStore store, CancellationToken ct) =>
            new ConfigDto(await store.ReadRawAsync(ct)));
        api.MapPut("/sources/config", SaveConfig);

        api.MapMovies();
        api.MapBrowse();
        api.MapSeries();
        api.MapLibrary();
    }

    private static async Task<IResult> Search(string? q, AggregationService service, CancellationToken ct)
    {
        var query = q?.Trim() ?? "";
        if (query.Length is < MinQueryLength or > MaxQueryLength)
        {
            return Results.Problem(
                $"Запрос должен содержать от {MinQueryLength} до {MaxQueryLength} символов",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return Results.Ok(await service.SearchAsync(query, ct));
    }

    private static async Task<IResult> GetTitle(int id, AggregationService service, CancellationToken ct)
    {
        var details = await service.GetTitleAsync(id, ct);
        return details is null ? Results.NotFound() : Results.Ok(details);
    }

    private static async Task<IResult> GetPlayback(
        int id,
        string source,
        string dub,
        int episode,
        AggregationService service,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (episode < 1)
        {
            return Results.Problem("Номер серии должен быть больше нуля", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var playback = await service.GetPlaybackAsync(id, source, dub, episode, ct);
            return playback is null ? Results.NotFound() : Results.Ok(playback);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            loggers.CreateLogger("Playback").LogWarning(ex, "Источник {Source} не отдал поток", source);
            return Results.Problem($"Источник {source} не отвечает", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IReadOnlyList<HistoryItem>> GetHistory(AppDbContext db, CancellationToken ct)
    {
        var items = await db.WatchProgress
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(HistoryLimit)
            .Join(db.CachedTitles, p => p.TitleId, t => t.Id, (p, t) => new { Progress = p, Title = t })
            .ToListAsync(ct);

        return items.Select(i => ToHistoryItem(i.Progress, i.Title)).ToList();
    }

    private static async Task<IResult> GetProgress(int id, AppDbContext db, CancellationToken ct)
    {
        var progress = await db.WatchProgress.AsNoTracking().FirstOrDefaultAsync(p => p.TitleId == id, ct);
        var title = progress is null ? null : await db.CachedTitles.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        return progress is null || title is null ? Results.NotFound() : Results.Ok(ToHistoryItem(progress, title));
    }

    private static async Task<IResult> SaveProgress(
        int id,
        SaveProgressRequest request,
        AppDbContext db,
        ICatalog catalog,
        CancellationToken ct)
    {
        if (request.Episode < 1 || string.IsNullOrWhiteSpace(request.DubKey) || string.IsNullOrWhiteSpace(request.SourceId))
        {
            return Results.Problem("Некорректные данные прогресса", statusCode: StatusCodes.Status400BadRequest);
        }

        // Заодно кладёт карточку тайтла в кэш: она нужна блоку «Продолжить просмотр».
        if (await catalog.GetAsync(id, ct) is null) return Results.NotFound();

        var progress = await db.WatchProgress.FirstOrDefaultAsync(p => p.TitleId == id, ct);
        if (progress is null)
        {
            progress = new WatchProgress { TitleId = id };
            db.WatchProgress.Add(progress);
        }

        progress.MoveTo(request.Episode, request.DubKey, request.DubName, request.SourceId, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SavePosition(
        int id,
        SavePositionRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        if (request.Episode < 1
            || request.Seconds is < 0 or > MaxPositionSeconds
            || request.DurationSeconds is < 0 or > MaxPositionSeconds)
        {
            return Results.Problem("Некорректная позиция просмотра", statusCode: StatusCodes.Status400BadRequest);
        }

        var progress = await db.WatchProgress.FirstOrDefaultAsync(p => p.TitleId == id, ct);
        if (progress is null) return Results.NotFound();

        if (progress.TryUpdatePosition(request.Episode, request.Seconds, request.DurationSeconds, DateTimeOffset.UtcNow))
        {
            await db.SaveChangesAsync(ct);
        }

        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<SourceStatus>> CheckSources(SourceHealthService health, CancellationToken ct)
    {
        await health.CheckAllAsync(ct);
        return health.GetStatuses();
    }

    private static async Task<IResult> SetEnabled(
        string id,
        SetEnabledRequest request,
        SourcesConfigStore store,
        SourceHealthService health,
        CancellationToken ct)
    {
        try
        {
            await store.SetEnabledAsync(id, request.Enabled, ct);
        }
        catch (SourcesConfigException ex)
        {
            return ConfigProblem(ex);
        }

        await health.CheckAllAsync(ct);
        return Results.Ok(health.GetStatuses());
    }

    private static async Task<IResult> SaveConfig(
        ConfigDto request,
        SourcesConfigStore store,
        SourceHealthService health,
        CancellationToken ct)
    {
        try
        {
            await store.SaveRawAsync(request.Yaml, ct);
        }
        catch (SourcesConfigException ex)
        {
            return ConfigProblem(ex);
        }

        await health.CheckAllAsync(ct);
        return Results.Ok(health.GetStatuses());
    }

    private static IResult ConfigProblem(SourcesConfigException ex) =>
        Results.Problem(
            "Конфиг не сохранён",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["errors"] = ex.Errors });

    private static HistoryItem ToHistoryItem(WatchProgress progress, CachedTitle title) => new(
        title.Id,
        title.Name,
        title.PosterUrl,
        progress.LastEpisode,
        title.EpisodesTotal,
        progress.WatchedEpisodes.Count,
        progress.DubKey,
        progress.DubName,
        progress.SourceId,
        progress.WatchedEpisodes,
        progress.PositionSeconds,
        progress.DurationSeconds);
}
