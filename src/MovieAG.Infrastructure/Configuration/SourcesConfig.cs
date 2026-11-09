namespace MovieAG.Infrastructure.Configuration;

public sealed class SourcesConfig
{
    public CatalogDefinition Catalog { get; set; } = new();
    public List<SourceDefinition> Sources { get; set; } = [];
    public MoviesConfig Movies { get; set; } = new();
}

public sealed class CatalogDefinition
{
    public string Provider { get; set; } = "shikimori";
    public string Url { get; set; } = "https://shikimori.io";
}

public sealed class MoviesConfig
{
    public MovieCatalogDefinition Catalog { get; set; } = new();
    public List<MovieSourceDefinition> Sources { get; set; } = [];
}

/// <summary>Настройки TMDB; без токена карточки строятся из данных источников.</summary>
public sealed class MovieCatalogDefinition
{
    public string Url { get; set; } = "https://api.themoviedb.org";
    public string ImagesUrl { get; set; } = "https://image.tmdb.org/t/p/w500";
    public string? Token { get; set; }
}

public sealed class MovieSourceDefinition : SourceDefinition
{
    public List<LibraryMovie> Items { get; set; } = [];
}

/// <summary>Фильм в библиотеке. Несколько записей с одним imdbId становятся вариантами просмотра.</summary>
public sealed class LibraryMovie
{
    public string ImdbId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? OriginalName { get; set; }
    public int? Year { get; set; }
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public List<string> Genres { get; set; } = [];
    public string Label { get; set; } = "Оригинал";
    public string? License { get; set; }
    public string Url { get; set; } = "";
}

public class SourceDefinition
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Url { get; set; }
    public string? Token { get; set; }

    /// <summary>Имя адаптера; по умолчанию совпадает с Id.</summary>
    public string? Adapter { get; set; }

    public bool Enabled { get; set; } = true;

    public string AdapterName => string.IsNullOrWhiteSpace(Adapter) ? Id : Adapter;
}

public sealed class SourcesConfigException(IReadOnlyList<string> errors)
    : Exception(string.Join("; ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
