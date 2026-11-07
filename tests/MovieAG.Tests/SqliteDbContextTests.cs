using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MovieAG.Core;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Tests;

/// <summary>Проверяет desktop-базу на настоящем SQLite в памяти, с её собственными миграциями.</summary>
public sealed class SqliteDbContextTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private SqliteDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new SqliteDbContext(new DbContextOptionsBuilder<SqliteDbContext>().UseSqlite(_connection).Options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Migrations_CreateSchemaWithoutPendingChanges()
    {
        Assert.Empty(await _db.Database.GetPendingMigrationsAsync());
        Assert.False(_db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task StoresListsAndPosition()
    {
        var title = new CachedTitle { Id = 1 };
        title.Apply(new CatalogTitle { Id = 1, Name = "Фрирен", Genres = ["Приключения", "Драма"] }, DateTimeOffset.UtcNow);
        var progress = new WatchProgress { TitleId = 1 };
        progress.MoveTo(1, "anilibria", "AniLibria", "anilibria", DateTimeOffset.UtcNow);
        progress.MoveTo(2, "anilibria", "AniLibria", "anilibria", DateTimeOffset.UtcNow);
        progress.TryUpdatePosition(2, 754, 1470, DateTimeOffset.UtcNow);
        _db.AddRange(title, progress);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var savedTitle = await _db.CachedTitles.SingleAsync();
        var savedProgress = await _db.WatchProgress.SingleAsync();

        Assert.Equal(["Приключения", "Драма"], savedTitle.Genres);
        Assert.Equal([1], savedProgress.WatchedEpisodes);
        Assert.Equal(754, savedProgress.PositionSeconds);
        Assert.Equal(1470, savedProgress.DurationSeconds);
    }

    /// <summary>Тот же запрос, что у истории просмотра: SQLite сам по себе не сортирует DateTimeOffset.</summary>
    [Fact]
    public async Task HistoryQuery_SortsByLastUpdate()
    {
        var start = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
        for (var id = 1; id <= 3; id++)
        {
            var title = new CachedTitle { Id = id };
            title.Apply(new CatalogTitle { Id = id, Name = $"Тайтл {id}" }, start);
            var progress = new WatchProgress { TitleId = id };
            progress.MoveTo(1, "dub", "Dub", "src", start.AddMinutes(id == 2 ? 30 : id));
            _db.AddRange(title, progress);
        }
        await _db.SaveChangesAsync();

        var order = await _db.WatchProgress
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(6)
            .Join(_db.CachedTitles, p => p.TitleId, t => t.Id, (p, t) => t.Id)
            .ToListAsync();

        Assert.Equal([2, 3, 1], order);
    }
}
