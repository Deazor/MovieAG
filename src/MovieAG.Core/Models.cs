namespace MovieAG.Core;

public enum DubKind { Voice, Subtitles }

public enum PlaybackKind { Iframe, Hls }

/// <summary>Метаданные тайтла из каталога; Id совпадает с ID Shikimori.</summary>
public sealed record CatalogTitle
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public string? JapaneseName { get; init; }
    public string? Kind { get; init; }
    public int? Year { get; init; }
    public int? EpisodesTotal { get; init; }
    public string? Status { get; init; }
    public string? Description { get; init; }
    public string? PosterUrl { get; init; }
    public string? Studio { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
}

/// <summary>Тайтл в выдаче одного источника.</summary>
public sealed record SourceTitle
{
    public required int ShikimoriId { get; init; }
    public required string SourceId { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public string? Kind { get; init; }
    public int? Year { get; init; }
    public int? EpisodesTotal { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<string> DubNames { get; init; } = [];
    public int? LatestEpisode { get; init; }
}

public sealed record TitleCard
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? OriginalName { get; init; }
    public string? Kind { get; init; }
    public int? Year { get; init; }
    public int? EpisodesTotal { get; init; }
    public string? PosterUrl { get; init; }
    public IReadOnlyList<string> Dubs { get; init; } = [];
    public IReadOnlyList<string> Sources { get; init; } = [];
    public int? LatestEpisode { get; init; }
}

/// <summary>Одна озвучка у одного источника.</summary>
public sealed record DubVariant(
    string SourceId,
    string DubId,
    string DubName,
    DubKind Kind,
    IReadOnlyList<int> Episodes);

public sealed record DubSource(string SourceId, string DubId, IReadOnlyList<int> Episodes);

/// <summary>Озвучка, собранная со всех источников.</summary>
public sealed record DubGroup(
    string Key,
    string Name,
    DubKind Kind,
    IReadOnlyList<int> Episodes,
    IReadOnlyList<DubSource> Sources);

public sealed record PlaybackQuality(string Label, string Url);

public sealed record Playback(PlaybackKind Kind, string Url, IReadOnlyList<PlaybackQuality> Qualities);

public sealed record SearchResult(
    IReadOnlyList<TitleCard> Items,
    IReadOnlyList<string> QueriedSources,
    IReadOnlyList<string> FailedSources);

public sealed record TitleDetails(
    CatalogTitle Title,
    IReadOnlyList<DubGroup> Dubs,
    IReadOnlyList<string> FailedSources);
