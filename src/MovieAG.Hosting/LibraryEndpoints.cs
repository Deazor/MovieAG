using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using MovieAG.Core;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Hosting;

/// <summary>Отметка тайтла в библиотеке. Без статуса и без «Любимого» запись удаляется.</summary>
public sealed record LibraryState(LibraryStatus? Status, bool Favorite);

/// <summary>Сохранение отметки: вместе с ней приходят название и постер для страницы «Библиотека».</summary>
public sealed record SaveLibraryRequest(LibraryStatus? Status, bool Favorite, string Name, string? PosterUrl, int? Year);

public sealed record LibraryItemDto(
    LibraryKind Kind,
    string ItemId,
    string Name,
    string? PosterUrl,
    int? Year,
    LibraryStatus? Status,
    bool Favorite);

public sealed record LibraryCounts(int Planned, int Watched, int Favorite);

public sealed record LibraryPage(IReadOnlyList<LibraryItemDto> Items, int Page, bool HasMore, LibraryCounts Counts);

/// <summary>Библиотека: «Буду смотреть», «Просмотрено» и «Любимое» для аниме, фильмов и сериалов.</summary>
internal static class LibraryEndpoints
{
    private const int PageSize = 24;
    private const int MaxPage = 1000;
    private const string ItemRoute = "/{kind}/{itemId}";

    public static void MapLibrary(this RouteGroupBuilder api)
    {
        var library = api.MapGroup("/library");

        library.MapGet("/", List);
        library.MapGet(ItemRoute, GetState);
        library.MapPut(ItemRoute, Save);
    }

    /// <summary>Список одной вкладки, новые отметки сверху; kind сужает выдачу до раздела.</summary>
    /// <remarks>
    /// Параметры берутся строками: привязка enum в minimal API чувствительна к регистру,
    /// а клиент присылает их как в JSON — planned, movie.
    /// </remarks>
    private static async Task<IResult> List(string? list, string? kind, int? page, AppDbContext db, CancellationToken ct)
    {
        var pageNumber = page ?? 1;
        LibraryList? parsedList = string.IsNullOrEmpty(list) ? LibraryList.Planned : ParseEnum<LibraryList>(list);
        LibraryKind? parsedKind = string.IsNullOrEmpty(kind) ? null : ParseEnum<LibraryKind>(kind);

        if (pageNumber is < 1 or > MaxPage || parsedList is null || (!string.IsNullOrEmpty(kind) && parsedKind is null))
        {
            return Results.Problem(
                $"Список: planned, watched или favorite; раздел: anime, movie или series; страница от 1 до {MaxPage}",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var all = db.Library.AsNoTracking();
        if (parsedKind is { } k) all = all.Where(i => i.Kind == k);

        var selected = parsedList switch
        {
            LibraryList.Watched => all.Where(i => i.Status == LibraryStatus.Watched),
            LibraryList.Favorite => all.Where(i => i.Favorite),
            _ => all.Where(i => i.Status == LibraryStatus.Planned)
        };

        // Берём на одну запись больше страницы, чтобы узнать, есть ли следующая.
        var items = await selected
            .OrderByDescending(i => i.UpdatedAt)
            .Skip((pageNumber - 1) * PageSize)
            .Take(PageSize + 1)
            .ToListAsync(ct);

        var counts = new LibraryCounts(
            await all.CountAsync(i => i.Status == LibraryStatus.Planned, ct),
            await all.CountAsync(i => i.Status == LibraryStatus.Watched, ct),
            await all.CountAsync(i => i.Favorite, ct));

        return Results.Ok(new LibraryPage(items.Take(PageSize).Select(ToDto).ToList(), pageNumber, items.Count > PageSize, counts));
    }

    private static async Task<IResult> GetState(string kind, string itemId, AppDbContext db, CancellationToken ct)
    {
        if (ParseKey(kind, itemId) is not { } key) return InvalidKey();

        var item = await db.Library.AsNoTracking().FirstOrDefaultAsync(i => i.Kind == key && i.ItemId == itemId, ct);
        return Results.Ok(item is null ? new LibraryState(null, false) : new LibraryState(item.Status, item.Favorite));
    }

    private static async Task<IResult> Save(string kind, string itemId, SaveLibraryRequest request, AppDbContext db, CancellationToken ct)
    {
        if (ParseKey(kind, itemId) is not { } key) return InvalidKey();
        if (LibraryRules.Validate(request.Name, request.PosterUrl, request.Year) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var item = await db.Library.FirstOrDefaultAsync(i => i.Kind == key && i.ItemId == itemId, ct);
        var empty = request.Status is null && !request.Favorite;

        if (empty)
        {
            if (item is not null) db.Library.Remove(item);
        }
        else
        {
            if (item is null)
            {
                item = new LibraryItem { Kind = key, ItemId = itemId };
                db.Library.Add(item);
            }

            item.Name = request.Name.Trim();
            item.PosterUrl = request.PosterUrl;
            item.Year = request.Year;
            item.Status = request.Status;
            item.Favorite = request.Favorite;
            item.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new LibraryState(request.Status, request.Favorite));
    }

    /// <summary>Раздел приходит строкой (anime, movie, series), ID проверяется по правилам раздела.</summary>
    private static LibraryKind? ParseKey(string kind, string itemId) =>
        ParseEnum<LibraryKind>(kind) is { } parsed && LibraryRules.IsValidItemId(parsed, itemId) ? parsed : null;

    /// <summary>Только имя значения без учёта регистра: числа вроде «7» Enum.TryParse тоже принял бы.</summary>
    private static T? ParseEnum<T>(string value) where T : struct, Enum =>
        !value.All(char.IsAsciiDigit) && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;

    private static IResult InvalidKey() =>
        Results.Problem("Неизвестный раздел или ID", statusCode: StatusCodes.Status400BadRequest);

    private static LibraryItemDto ToDto(LibraryItem item) =>
        new(item.Kind, item.ItemId, item.Name, item.PosterUrl, item.Year, item.Status, item.Favorite);
}
