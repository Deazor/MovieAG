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

    [Fact]
    public void MoveTo_ChangingDubOnSameEpisodeKeepsPosition()
    {
        var progress = new WatchProgress();
        progress.MoveTo(5, "a", "A", "src", Now);
        progress.TryUpdatePosition(5, 754, 1440, Now);

        progress.MoveTo(5, "b", "B", "src", Now);

        Assert.Equal(754, progress.PositionSeconds);
        Assert.Equal(1440, progress.DurationSeconds);
    }

    [Fact]
    public void MoveTo_ChangingEpisodeResetsPosition()
    {
        var progress = new WatchProgress();
        progress.MoveTo(5, "a", "A", "src", Now);
        progress.TryUpdatePosition(5, 754, 1440, Now);

        progress.MoveTo(6, "a", "A", "src", Now);

        Assert.Equal(0, progress.PositionSeconds);
        Assert.Null(progress.DurationSeconds);
    }

    [Fact]
    public void TryUpdatePosition_IgnoresReportFromAnotherEpisode()
    {
        var progress = new WatchProgress();
        progress.MoveTo(6, "a", "A", "src", Now);

        Assert.False(progress.TryUpdatePosition(5, 754, 1440, Now));
        Assert.Equal(0, progress.PositionSeconds);
    }

    [Fact]
    public void TryUpdatePosition_KeepsKnownDurationWhenNotReported()
    {
        var progress = new WatchProgress();
        progress.MoveTo(5, "a", "A", "src", Now);
        progress.TryUpdatePosition(5, 10, 1440, Now);

        Assert.True(progress.TryUpdatePosition(5, 20, null, Now));

        Assert.Equal(20, progress.PositionSeconds);
        Assert.Equal(1440, progress.DurationSeconds);
    }
}
