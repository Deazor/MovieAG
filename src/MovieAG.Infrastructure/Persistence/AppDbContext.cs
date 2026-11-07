using Microsoft.EntityFrameworkCore;
using MovieAG.Core;

namespace MovieAG.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<CachedTitle> CachedTitles => Set<CachedTitle>();
    public DbSet<WatchProgress> WatchProgress => Set<WatchProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CachedTitle>(entity =>
        {
            entity.ToTable("cached_titles");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Id).ValueGeneratedNever();
            entity.Property(t => t.Name).HasMaxLength(500);
        });

        modelBuilder.Entity<WatchProgress>(entity =>
        {
            entity.ToTable("watch_progress");
            entity.HasKey(p => p.TitleId);
            entity.Property(p => p.TitleId).ValueGeneratedNever();
            entity.Property(p => p.DubKey).HasMaxLength(200);
            entity.Property(p => p.DubName).HasMaxLength(200);
            entity.Property(p => p.SourceId).HasMaxLength(100);
            entity.HasIndex(p => p.UpdatedAt);
        });
    }
}

public sealed class CachedTitle
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? OriginalName { get; set; }
    public string? JapaneseName { get; set; }
    public string? Kind { get; set; }
    public int? Year { get; set; }
    public int? EpisodesTotal { get; set; }
    public string? Status { get; set; }
    public string? Description { get; set; }
    public string? PosterUrl { get; set; }
    public string? Studio { get; set; }
    public List<string> Genres { get; set; } = [];
    public DateTimeOffset FetchedAt { get; set; }

    public void Apply(CatalogTitle title, DateTimeOffset fetchedAt)
    {
        Name = title.Name;
        OriginalName = title.OriginalName;
        JapaneseName = title.JapaneseName;
        Kind = title.Kind;
        Year = title.Year;
        EpisodesTotal = title.EpisodesTotal;
        Status = title.Status;
        Description = title.Description;
        PosterUrl = title.PosterUrl;
        Studio = title.Studio;
        Genres = title.Genres.ToList();
        FetchedAt = fetchedAt;
    }

    public CatalogTitle ToModel() => new()
    {
        Id = Id,
        Name = Name,
        OriginalName = OriginalName,
        JapaneseName = JapaneseName,
        Kind = Kind,
        Year = Year,
        EpisodesTotal = EpisodesTotal,
        Status = Status,
        Description = Description,
        PosterUrl = PosterUrl,
        Studio = Studio,
        Genres = Genres
    };
}

/// <summary>История просмотра одного тайтла. Приложение локальное: один пользователь, одна строка на тайтл.</summary>
public sealed class WatchProgress
{
    public int TitleId { get; set; }
    public int LastEpisode { get; set; }
    public List<int> WatchedEpisodes { get; set; } = [];
    public string DubKey { get; set; } = "";
    public string DubName { get; set; } = "";
    public string SourceId { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Серия считается просмотренной, когда пользователь переходит с неё на другую.</summary>
    public void MoveTo(int episode, string dubKey, string dubName, string sourceId, DateTimeOffset now)
    {
        if (LastEpisode > 0 && LastEpisode != episode && !WatchedEpisodes.Contains(LastEpisode))
        {
            WatchedEpisodes = [.. WatchedEpisodes, LastEpisode];
        }

        LastEpisode = episode;
        DubKey = dubKey;
        DubName = dubName;
        SourceId = sourceId;
        UpdatedAt = now;
    }
}
