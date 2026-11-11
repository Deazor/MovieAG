namespace MovieAG.Core;

/// <summary>Порядок выдачи каталога: по дате выхода (сначала новые), по рейтингу или по популярности.</summary>
public enum BrowseSort { Date, Rating, Popularity }

/// <summary>Выбор тайтлов из каталога: текст, жанр и годы выхода необязательны, страницы считаются с 1.</summary>
public sealed record BrowseQuery(string? Text, string? Genre, int? YearFrom, int? YearTo, BrowseSort Sort, int Page)
{
    public const int MaxPage = 500;
    private const int MaxTextLength = 200;
    private const int MaxGenreLength = 10;
    private const int EarliestYear = 1900;
    private const int YearsAhead = 5;

    /// <summary>
    /// Проверяет параметры адреса и собирает из них запрос. Жанр — числовой ID каталога,
    /// сортировка — date, rating или popularity (по умолчанию date), страница — с 1.
    /// </summary>
    public static BrowseQuery? TryCreate(
        string? text, string? genre, int? from, int? to, string? sort, int? page, int currentYear, out string? error)
    {
        var latestYear = currentYear + YearsAhead;
        var parsedSort = ParseSort(sort);
        error = null;

        if (text?.Trim().Length > MaxTextLength) error = $"Запрос длиннее {MaxTextLength} символов";
        else if (!string.IsNullOrEmpty(genre) && (genre.Length > MaxGenreLength || !genre.All(char.IsAsciiDigit))) error = "Неизвестный жанр";
        else if (from is < EarliestYear || from > latestYear) error = $"Год «с» должен быть от {EarliestYear} до {latestYear}";
        else if (to is < EarliestYear || to > latestYear) error = $"Год «по» должен быть от {EarliestYear} до {latestYear}";
        else if (from > to) error = "Год «с» больше года «по»";
        else if (parsedSort is null) error = "Сортировка: date, rating или popularity";
        else if (page is < 1 or > MaxPage) error = $"Страница должна быть от 1 до {MaxPage}";

        if (error is not null) return null;

        return new BrowseQuery(
            string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            string.IsNullOrEmpty(genre) ? null : genre,
            from,
            to,
            parsedSort!.Value,
            page ?? 1);
    }

    private static BrowseSort? ParseSort(string? sort) => sort?.ToLowerInvariant() switch
    {
        null or "" or "date" => BrowseSort.Date,
        "rating" => BrowseSort.Rating,
        "popularity" => BrowseSort.Popularity,
        _ => null
    };
}

public sealed record Genre(string Id, string Name);

/// <summary>Одна страница каталога. Общее число страниц каталоги сообщают не всегда, поэтому только признак продолжения.</summary>
public sealed record BrowsePage<T>(IReadOnlyList<T> Items, int Page, bool HasMore);

/// <summary>Каталог аниме с жанрами и постраничной выборкой.</summary>
public interface IAnimeBrowser
{
    Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken ct);

    Task<BrowsePage<CatalogTitle>> BrowseAsync(BrowseQuery query, CancellationToken ct);
}

/// <summary>Каталог фильмов с жанрами и постраничной выборкой.</summary>
public interface IMovieBrowser
{
    Task<IReadOnlyList<Genre>> GetGenresAsync(CancellationToken ct);

    Task<BrowsePage<MovieTitle>> BrowseAsync(BrowseQuery query, CancellationToken ct);
}

/// <summary>Каталог не настроен (например, не задан токен), поэтому выборка недоступна.</summary>
public sealed class CatalogUnavailableException(string message) : Exception(message);
