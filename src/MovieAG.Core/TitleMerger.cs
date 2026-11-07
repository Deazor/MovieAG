using System.Text;

namespace MovieAG.Core;

/// <summary>Склейка выдачи разных источников в один тайтл и одну озвучку.</summary>
public static class TitleMerger
{
    /// <summary>
    /// Объединяет выдачу по ID Shikimori. Порядок: сначала совпадения каталога, затем остальные.
    /// Тайтлы каталога, которых нет ни в одном источнике, отбрасываются: смотреть их негде.
    /// </summary>
    public static IReadOnlyList<TitleCard> MergeCards(
        IReadOnlyList<CatalogTitle> catalog,
        IReadOnlyList<SourceTitle> found)
    {
        var bySource = found.ToLookup(t => t.ShikimoriId);
        var catalogById = catalog.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());

        var catalogOrder = catalog.Select(t => t.Id).Where(id => bySource.Contains(id));
        var orderedIds = catalogOrder.Concat(found.Select(t => t.ShikimoriId)).Distinct();

        return orderedIds
            .Select(id => BuildCard(id, catalogById.GetValueOrDefault(id), bySource[id].ToList()))
            .ToList();
    }

    public static IReadOnlyList<DubGroup> GroupDubs(IEnumerable<DubVariant> variants)
    {
        return variants
            .GroupBy(v => NormalizeDubName(v.DubName))
            .Select(g =>
            {
                var first = g.First();
                return new DubGroup(
                    g.Key,
                    first.DubName,
                    first.Kind,
                    g.SelectMany(v => v.Episodes).Distinct().Order().ToList(),
                    g.Select(v => new DubSource(v.SourceId, v.DubId, v.Episodes)).ToList());
            })
            .ToList();
    }

    /// <summary>Приводит «AniLibria.TV» и «AniLibria» к одному ключу.</summary>
    public static string NormalizeDubName(string name)
    {
        var key = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) key.Append(char.ToLowerInvariant(c));
        }

        var normalized = key.ToString();
        return normalized.Length > 2 && normalized.EndsWith("tv", StringComparison.Ordinal)
            ? normalized[..^2]
            : normalized;
    }

    private static TitleCard BuildCard(int id, CatalogTitle? meta, IReadOnlyList<SourceTitle> hits)
    {
        var first = hits[0];
        return new TitleCard
        {
            Id = id,
            Name = meta?.Name ?? first.Name,
            OriginalName = meta?.OriginalName ?? first.OriginalName,
            Kind = meta?.Kind ?? first.Kind,
            Year = meta?.Year ?? first.Year,
            EpisodesTotal = meta?.EpisodesTotal ?? first.EpisodesTotal,
            PosterUrl = meta?.PosterUrl ?? hits.Select(h => h.PosterUrl).FirstOrDefault(u => u is not null),
            Dubs = hits
                .SelectMany(h => h.DubNames)
                .DistinctBy(NormalizeDubName)
                .ToList(),
            Sources = hits.Select(h => h.SourceId).Distinct().ToList(),
            LatestEpisode = hits.Max(h => h.LatestEpisode)
        };
    }
}
