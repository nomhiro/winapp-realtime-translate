namespace RealtimeTranslate.Tests;

public class HistoryFollowStateTests
{
    [Fact]
    public void StartsFollowingAndStopsWhenScrolledAwayFromBottom()
    {
        var state = new HistoryFollowState();

        Assert.True(state.IsFollowing);

        state.Update(verticalOffset: 120, viewportHeight: 200, extentHeight: 600);

        Assert.False(state.IsFollowing);
    }

    [Fact]
    public void ResumesFollowingAtBottomWithinTolerance()
    {
        var state = new HistoryFollowState();
        state.Update(verticalOffset: 120, viewportHeight: 200, extentHeight: 600);

        state.Update(verticalOffset: 399.9995, viewportHeight: 200, extentHeight: 600);

        Assert.True(state.IsFollowing);
    }

    [Fact]
    public void OneItemAboveBottomIsNotFollowing()
    {
        var state = new HistoryFollowState();

        state.Update(verticalOffset: 399, viewportHeight: 200, extentHeight: 600);

        Assert.False(state.IsFollowing);
    }

    [Fact]
    public void ShortContentCountsAsAtBottom()
    {
        var state = new HistoryFollowState();

        state.Update(verticalOffset: 0, viewportHeight: 300, extentHeight: 300);

        Assert.True(state.IsFollowing);
    }

    [Fact]
    public void ReturnToLatestRestoresFollowing()
    {
        var state = new HistoryFollowState();
        state.Update(verticalOffset: 0, viewportHeight: 200, extentHeight: 600);

        state.ReturnToLatest();

        Assert.True(state.IsFollowing);
    }

    [Fact]
    public void PauseStopsFollowingBeforeTheScrollIsApplied()
    {
        var state = new HistoryFollowState();

        state.Pause();

        Assert.False(state.IsFollowing);
    }

    [Fact]
    public void HistoriesKeepIndependentFollowState()
    {
        var subtitles = new HistoryFollowState();
        var screenTexts = new HistoryFollowState();

        subtitles.Update(verticalOffset: 0, viewportHeight: 200, extentHeight: 600);

        Assert.False(subtitles.IsFollowing);
        Assert.True(screenTexts.IsFollowing);
    }
}
