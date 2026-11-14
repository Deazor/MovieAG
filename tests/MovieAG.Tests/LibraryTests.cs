using MovieAG.Core;

namespace MovieAG.Tests;

public class LibraryRulesTests
{
    [Theory]
    [InlineData(LibraryKind.Anime, "59193", true)]
    [InlineData(LibraryKind.Anime, "0123", false)]
    [InlineData(LibraryKind.Anime, "tt0903747", false)]
    [InlineData(LibraryKind.Anime, "12345678901", false)]
    [InlineData(LibraryKind.Movie, "tt0245429", true)]
    [InlineData(LibraryKind.Movie, "59193", false)]
    [InlineData(LibraryKind.Series, "tt0903747", true)]
    [InlineData(LibraryKind.Series, "../etc", false)]
    [InlineData(LibraryKind.Series, null, false)]
    public void IsValidItemId_DependsOnKind(LibraryKind kind, string? itemId, bool expected)
    {
        Assert.Equal(expected, LibraryRules.IsValidItemId(kind, itemId));
    }

    [Theory]
    [InlineData("Фрирен", "https://shikimori.io/p.jpg", 2023, true)]
    [InlineData("Фрирен", null, null, true)]
    [InlineData("", null, null, false)]
    [InlineData("Фрирен", "javascript:alert(1)", null, false)]
    [InlineData("Фрирен", "/relative.jpg", null, false)]
    [InlineData("Фрирен", null, 1800, false)]
    public void Validate_AcceptsOnlyNameHttpPosterAndSaneYear(string name, string? posterUrl, int? year, bool isValid)
    {
        Assert.Equal(isValid, LibraryRules.Validate(name, posterUrl, year) is null);
    }

    [Fact]
    public void Validate_RejectsTooLongName()
    {
        Assert.NotNull(LibraryRules.Validate(new string('а', LibraryRules.MaxNameLength + 1), null, null));
    }
}
