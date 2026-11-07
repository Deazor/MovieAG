using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MovieAG.Infrastructure.Persistence;

/// <summary>
/// Контекст для desktop-версии. Миграции у него свои, в Persistence/SqliteMigrations.
/// </summary>
public sealed class SqliteDbContext(DbContextOptions<SqliteDbContext> options) : AppDbContext(options)
{
    /// <summary>SQLite не умеет сортировать DateTimeOffset, а история сортируется по дате.</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }
}

/// <summary>Нужен только инструменту dotnet ef для создания миграций SQLite.</summary>
public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteDbContext>
{
    public SqliteDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteDbContext>().UseSqlite("Data Source=design.db").Options);
}
