using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MovieAG.Core;

namespace MovieAG.Infrastructure.Persistence;

/// <summary>Контекст для PostgreSQL; наследники задают особенности других провайдеров.</summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected AppDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<CachedTitle> CachedTitles => Set<CachedTitle>();
    public DbSet<WatchProgress> WatchProgress => Set<WatchProgress>();
    public DbSet<MovieProgress> MovieProgress => Set<MovieProgress>();

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

        modelBuilder.Entity<MovieProgress>(entity =>
        {
            entity.ToTable("movie_progress");
            entity.HasKey(p => p.ImdbId);
            entity.Property(p => p.ImdbId).HasMaxLength(16).ValueGeneratedNever();
            entity.Property(p => p.Name).HasMaxLength(500);
            entity.Property(p => p.SourceId).HasMaxLength(100);
            entity.Property(p => p.StreamId).HasMaxLength(200);
            entity.HasIndex(p => p.UpdatedAt);
        });
    }
}

/// <summary>
/// Нужен только инструменту dotnet ef. Без него для AppDbContext берётся фабрика наследника
/// (интерфейс фабрики ковариантен), и миграции PostgreSQL получают типы SQLite. Подключения не открывает.
/// </summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=design").Options);
}

/// <summary>Позиция просмотра фильма. Название и постер хранятся здесь, чтобы история не зависела от каталога.</summary>
public sealed class MovieProgress
{
    public string ImdbId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? PosterUrl { get; set; }
    public string SourceId { get; set; } = "";
    public string StreamId { get; set; } = "";
    public int PositionSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
    public double? Score { get; set; }
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
        Score = title.Score;
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
        Genres = Genres,
        Score = Score
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
    public int PositionSeconds { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Серия считается просмотренной, когда пользователь переходит с неё на другую.
    /// Смена озвучки или плеера на той же серии позицию не сбрасывает.
    /// </summary>
    public void MoveTo(int episode, string dubKey, string dubName, string sourceId, DateTimeOffset now)
    {
        if (LastEpisode != episode)
        {
            if (LastEpisode > 0 && !WatchedEpisodes.Contains(LastEpisode))
            {
                WatchedEpisodes = [.. WatchedEpisodes, LastEpisode];
            }

            PositionSeconds = 0;
            DurationSeconds = null;
        }

        LastEpisode = episode;
        DubKey = dubKey;
        DubName = dubName;
        SourceId = sourceId;
        UpdatedAt = now;
    }

    /// <summary>Отметка от старой серии может прийти после перехода на новую: такую не учитываем.</summary>
    public bool TryUpdatePosition(int episode, int seconds, int? durationSeconds, DateTimeOffset now)
    {
        if (episode != LastEpisode) return false;

        PositionSeconds = seconds;
        DurationSeconds = durationSeconds ?? DurationSeconds;
        UpdatedAt = now;
        return true;
    }
}
