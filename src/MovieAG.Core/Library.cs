namespace MovieAG.Core;

/// <summary>Раздел, к которому относится запись библиотеки; от него зависит формат ID.</summary>
public enum LibraryKind { Anime, Movie, Series }

/// <summary>«Буду смотреть» и «Просмотрено» взаимоисключающие; «Любимое» — отдельный флаг.</summary>
public enum LibraryStatus { Planned, Watched }

/// <summary>Списки страницы «Библиотека»: два статуса и избранное.</summary>
public enum LibraryList { Planned, Watched, Favorite }

public static class LibraryRules
{
    public const int MaxNameLength = 500;
    public const int MaxPosterUrlLength = 2000;
    private const int MaxAnimeIdLength = 10;
    private const int EarliestYear = 1900;
    private const int LatestYear = 2100;

    /// <summary>У аниме ID Shikimori (число), у фильмов и сериалов — IMDb ID.</summary>
    public static bool IsValidItemId(LibraryKind kind, string? itemId) => kind switch
    {
        LibraryKind.Anime => itemId is { Length: > 0 and <= MaxAnimeIdLength } && itemId.All(char.IsAsciiDigit) && itemId[0] != '0',
        _ => ImdbId.IsValid(itemId)
    };

    /// <summary>
    /// Проверяет то, что клиент присылает для карточки библиотеки. Постер выводится как src картинки,
    /// поэтому разрешены только абсолютные http(s)-адреса.
    /// </summary>
    public static string? Validate(string? name, string? posterUrl, int? year)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength) return "Название обязательно и не длиннее 500 символов";

        if (posterUrl is not null
            && (posterUrl.Length > MaxPosterUrlLength
                || !Uri.TryCreate(posterUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")))
        {
            return "Постер должен быть адресом http или https";
        }

        return year is < EarliestYear or > LatestYear ? "Некорректный год" : null;
    }
}
