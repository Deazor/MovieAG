using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Tests;

public class WatchProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MoveTo_FirstEpisodeMarksNothingWatched()
    {
        var progress = new WatchProgress();

        progress.MoveTo(3, "dub", "Dub", "src", Now);

        Assert.Equal(3, progress.LastEpisode);
        Assert.Empty(progress.WatchedEpisodes);
    }

    [Fact]
    public void MoveTo_MarksPreviousEpisodeWatchedOnce()
    {
        var progress = new WatchProgress();
        progress.MoveTo(1, "dub", "Dub", "src", Now);
        progress.MoveTo(2, "dub", "Dub", "src", Now);
        progress.MoveTo(1, "dub", "Dub", "src", Now);
        progress.MoveTo(2, "dub", "Dub", "src", Now);

        Assert.Equal([1, 2], progress.WatchedEpisodes);
    }

    [Fact]
    public void MoveTo_ChangingDubOnSameEpisodeDoesNotMarkItWatched()
    {
        var progress = new WatchProgress();
        progress.MoveTo(5, "a", "A", "src", Now);
        progress.MoveTo(5, "b", "B", "src", Now);

        Assert.Empty(progress.WatchedEpisodes);
        Assert.Equal("b", progress.DubKey);
    }
}
