using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using MovieAG.Core;

namespace MovieAG.Hosting;

/// <summary>Параметры адреса выборки: /api/browse?q=&amp;genre=22&amp;from=2015&amp;to=2020&amp;sort=date&amp;page=2.</summary>
public sealed record BrowseRequest(string? Q, string? Genre, int? From, int? To, string? Sort, int? Page)
{
    public BrowseQuery? ToQuery(int currentYear, out string? error) =>
        BrowseQuery.TryCreate(Q, Genre, From, To, Sort, Page, currentYear, out error);
}

public sealed record BrowseResult<T>(IReadOnlyList<T> Items, int Page, bool HasMore);

/// <summary>Выбор аниме и фильмов по жанру, годам выхода и сортировке, постранично.</summary>
internal static class BrowseEndpoints
{
    public static void MapBrowse(this RouteGroupBuilder api)
    {
        api.MapGet("/genres", GetAnimeGenres);
        api.MapGet("/browse", BrowseAnime);
        api.MapGet("/movies/genres", GetMovieGenres);
        api.MapGet("/movies/browse", BrowseMovies);
    }

    private static Task<IResult> GetAnimeGenres(IAnimeBrowser catalog, ILoggerFactory loggers, CancellationToken ct) =>
        CallCatalogAsync(async () => Results.Ok(await catalog.GetGenresAsync(ct)), loggers, ct);

    private static Task<IResult> GetMovieGenres(IMovieBrowser catalog, ILoggerFactory loggers, CancellationToken ct) =>
        CallCatalogAsync(async () => Results.Ok(await catalog.GetGenresAsync(ct)), loggers, ct);

    private static Task<IResult> BrowseAnime(
        [AsParameters] BrowseRequest request,
        IAnimeBrowser catalog,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var query = request.ToQuery(time.GetUtcNow().Year, out var error);
        if (query is null) return Task.FromResult(BadRequest(error!));

        return CallCatalogAsync(async () =>
        {
            var page = await catalog.BrowseAsync(query, ct);
            return Results.Ok(new BrowseResult<TitleCard>(page.Items.Select(ToCard).ToList(), page.Page, page.HasMore));
        }, loggers, ct);
    }

    private static Task<IResult> BrowseMovies(
        [AsParameters] BrowseRequest request,
        IMovieBrowser catalog,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var query = request.ToQuery(time.GetUtcNow().Year, out var error);
        if (query is null) return Task.FromResult(BadRequest(error!));

        return CallCatalogAsync(async () =>
        {
            var page = await catalog.BrowseAsync(query, ct);
            return Results.Ok(new BrowseResult<MovieCard>(page.Items.Select(ToCard).ToList(), page.Page, page.HasMore));
        }, loggers, ct);
    }

    /// <summary>Ненастроенный каталог — 503 с подсказкой, недоступный — 502; остальные ошибки пробрасываются.</summary>
    private static async Task<IResult> CallCatalogAsync(Func<Task<IResult>> call, ILoggerFactory loggers, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (CatalogUnavailableException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            loggers.CreateLogger("Browse").LogWarning(ex, "Каталог не ответил");
            return Results.Problem("Каталог не отвечает, повторите позже", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static IResult BadRequest(string error) => Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Наличие в источниках здесь не проверяется: это видно на странице тайтла.</summary>
    private static TitleCard ToCard(CatalogTitle title) => new()
    {
        Id = title.Id,
        Name = title.Name,
        OriginalName = title.OriginalName,
        Kind = title.Kind,
        Year = title.Year,
        EpisodesTotal = title.EpisodesTotal,
        PosterUrl = title.PosterUrl,
        Score = title.Score
    };

    private static MovieCard ToCard(MovieTitle title) =>
        new(title.ImdbId, title.Name, title.OriginalName, title.Year, title.PosterUrl, [], title.Ratings);
}
