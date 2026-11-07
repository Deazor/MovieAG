using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MovieAG.Core;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Infrastructure.Catalog;

/// <summary>
/// Бережёт лимиты Shikimori: списки держит в памяти, полные карточки — в PostgreSQL.
/// Если каталог недоступен, отдаёт устаревшую карточку из базы.
/// </summary>
public sealed class CachedCatalog(ShikimoriCatalog inner, IMemoryCache memory, AppDbContext db) : ICatalog
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DetailTtl = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<CatalogTitle>> SearchAsync(string query, CancellationToken ct)
    {
        var key = "catalog:search:" + query.Trim().ToLowerInvariant();
        if (memory.TryGetValue(key, out IReadOnlyList<CatalogTitle>? cached) && cached is not null) return cached;

        var result = await inner.SearchAsync(query, ct);
        memory.Set(key, result, ListTtl);
        return result;
    }

    public async Task<IReadOnlyList<CatalogTitle>> GetManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var key = "catalog:many:" + string.Join(',', ids.Order());
        if (memory.TryGetValue(key, out IReadOnlyList<CatalogTitle>? cached) && cached is not null) return cached;

        var result = await inner.GetManyAsync(ids, ct);
        memory.Set(key, result, ListTtl);
        return result;
    }

    public async Task<CatalogTitle?> GetAsync(int id, CancellationToken ct)
    {
        var entity = await db.CachedTitles.FindAsync([id], ct);
        if (entity is not null && DateTimeOffset.UtcNow - entity.FetchedAt < DetailTtl) return entity.ToModel();

        CatalogTitle? fresh;
        try
        {
            fresh = await inner.GetAsync(id, ct);
        }
        catch (HttpRequestException) when (entity is not null)
        {
            return entity.ToModel();
        }

        if (fresh is null) return entity?.ToModel();

        if (entity is null)
        {
            entity = new CachedTitle { Id = id };
            db.CachedTitles.Add(entity);
        }

        entity.Apply(fresh, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);
        return fresh;
    }
}
