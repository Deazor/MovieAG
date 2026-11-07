using MovieAG.Core;

namespace MovieAG.Tests;

public class TitleMergerTests
{
    private static SourceTitle Hit(int id, string source, params string[] dubs) => new()
    {
        ShikimoriId = id,
        SourceId = source,
        Name = $"{source}-{id}",
        DubNames = dubs
    };

    [Fact]
    public void MergeCards_JoinsSameTitleFromDifferentSources()
    {
        var cards = TitleMerger.MergeCards([], [Hit(1, "anilibria", "AniLibria"), Hit(1, "kodik", "AniLibria.TV", "AniDUB")]);

        var card = Assert.Single(cards);
        Assert.Equal(["anilibria", "kodik"], card.Sources);
        Assert.Equal(["AniLibria", "AniDUB"], card.Dubs);
    }

    [Fact]
    public void MergeCards_PrefersCatalogMetadata()
    {
        var catalog = new CatalogTitle { Id = 1, Name = "Название из каталога", PosterUrl = "https://cat/p.jpg" };
        var hit = Hit(1, "kodik", "AniDUB") with { PosterUrl = "https://src/p.jpg" };

        var card = Assert.Single(TitleMerger.MergeCards([catalog], [hit]));

        Assert.Equal("Название из каталога", card.Name);
        Assert.Equal("https://cat/p.jpg", card.PosterUrl);
    }

    [Fact]
    public void MergeCards_FallsBackToSourcePosterWhenCatalogHasNone()
    {
        var catalog = new CatalogTitle { Id = 1, Name = "Тайтл" };
        var hit = Hit(1, "anilibria") with { PosterUrl = "https://src/p.jpg" };

        Assert.Equal("https://src/p.jpg", Assert.Single(TitleMerger.MergeCards([catalog], [hit])).PosterUrl);
    }

    [Fact]
    public void MergeCards_DropsCatalogTitlesThatNoSourceHas()
    {
        var catalog = new[]
        {
            new CatalogTitle { Id = 1, Name = "Нигде нет" },
            new CatalogTitle { Id = 2, Name = "Есть" }
        };

        var card = Assert.Single(TitleMerger.MergeCards(catalog, [Hit(2, "kodik")]));

        Assert.Equal(2, card.Id);
    }

    [Fact]
    public void MergeCards_KeepsCatalogOrderThenRest()
    {
        var catalog = new[] { new CatalogTitle { Id = 2, Name = "b" }, new CatalogTitle { Id = 1, Name = "a" } };

        var cards = TitleMerger.MergeCards(catalog, [Hit(3, "x"), Hit(1, "x"), Hit(2, "x")]);

        Assert.Equal([2, 1, 3], cards.Select(c => c.Id));
    }

    [Fact]
    public void MergeCards_MarksTitlesWhereSourceDoesNotNameDubs()
    {
        var cards = TitleMerger.MergeCards([], [Hit(1, "anilibria", "AniLibria"), Hit(1, "yummy"), Hit(2, "anilibria", "AniLibria")]);

        Assert.True(cards[0].HasMoreDubs);
        Assert.False(cards[1].HasMoreDubs);
    }

    [Fact]
    public void GroupDubs_UsesPlayerLabelAndFallsBackToSourceId()
    {
        var group = Assert.Single(TitleMerger.GroupDubs(
        [
            new DubVariant("anilibria", "9542", "AniLibria", DubKind.Voice, [1]),
            new DubVariant("yummy", "1|4|d", "AniLibria", DubKind.Voice, [1], "Kodik")
        ]));

        Assert.Equal(["anilibria", "Kodik"], group.Sources.Select(s => s.Label));
    }

    [Theory]
    [InlineData("AniLibria.TV", "anilibria")]
    [InlineData("AniLibria", "anilibria")]
    [InlineData("Dream Cast", "dreamcast")]
    [InlineData("TV", "tv")]
    public void NormalizeDubName(string input, string expected)
    {
        Assert.Equal(expected, TitleMerger.NormalizeDubName(input));
    }

    [Fact]
    public void GroupDubs_MergesSameDubAndUnionsEpisodes()
    {
        var groups = TitleMerger.GroupDubs(
        [
            new DubVariant("anilibria", "9542", "AniLibria", DubKind.Voice, [1, 2]),
            new DubVariant("kodik", "610", "AniLibria.TV", DubKind.Voice, [2, 3]),
            new DubVariant("kodik", "1", "Субтитры", DubKind.Subtitles, [1])
        ]);

        Assert.Equal(2, groups.Count);
        var group = groups[0];
        Assert.Equal("AniLibria", group.Name);
        Assert.Equal([1, 2, 3], group.Episodes);
        Assert.Equal(["anilibria", "kodik"], group.Sources.Select(s => s.SourceId));
    }
}
