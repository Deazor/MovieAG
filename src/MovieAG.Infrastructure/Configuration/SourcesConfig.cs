namespace MovieAG.Infrastructure.Configuration;

public sealed class SourcesConfig
{
    public CatalogDefinition Catalog { get; set; } = new();
    public List<SourceDefinition> Sources { get; set; } = [];
}

public sealed class CatalogDefinition
{
    public string Provider { get; set; } = "shikimori";
    public string Url { get; set; } = "https://shikimori.io";
}

public sealed class SourceDefinition
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
