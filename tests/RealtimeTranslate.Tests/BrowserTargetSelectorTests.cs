using System.Drawing;
using RealtimeTranslate.Screen;

namespace RealtimeTranslate.Tests;

public class BrowserTargetSelectorTests
{
    private static readonly BrowserWindowCandidate FirstWindow =
        new(new IntPtr(1), new Rectangle(0, 0, 800, 600), "chrome", "Documentation");

    private static readonly BrowserWindowCandidate SecondWindow =
        new(new IntPtr(2), new Rectangle(10, 10, 1000, 700), "chrome", "Issue tracker");

    [Fact]
    public void Select_FollowsForegroundAndRetainsLastBrowserWhenForegroundIsNotBrowser()
    {
        var selector = new BrowserTargetSelector();
        BrowserWindowCandidate[] candidates = [FirstWindow, SecondWindow];

        Assert.Equal(FirstWindow, selector.Select(candidates, FirstWindow.Hwnd));
        Assert.Equal(SecondWindow, selector.Select(candidates, SecondWindow.Hwnd));
        Assert.Equal(SecondWindow, selector.Select(candidates, new IntPtr(99)));
    }

    [Fact]
    public void Select_PinnedTargetIgnoresForegroundAndReturnsNullWhenItDisappears()
    {
        var selector = new BrowserTargetSelector();
        BrowserWindowCandidate[] candidates = [FirstWindow, SecondWindow];

        Assert.True(selector.Pin(FirstWindow.Hwnd, candidates));
        Assert.Equal(FirstWindow, selector.Select(candidates, SecondWindow.Hwnd));
        Assert.Null(selector.Select([SecondWindow], SecondWindow.Hwnd));
    }

    [Fact]
    public void FollowAutomatically_UnpinsAndResumesFromPinnedWindow()
    {
        var selector = new BrowserTargetSelector();
        BrowserWindowCandidate[] candidates = [FirstWindow, SecondWindow];

        Assert.True(selector.Pin(FirstWindow.Hwnd, candidates));
        selector.FollowAutomatically();

        Assert.Null(selector.PinnedHwnd);
        Assert.Equal(FirstWindow, selector.Select(candidates, new IntPtr(99)));
    }

    [Fact]
    public void Pin_RejectsWindowThatIsNoLongerACandidate()
    {
        var selector = new BrowserTargetSelector();

        Assert.False(selector.Pin(FirstWindow.Hwnd, [SecondWindow]));
        Assert.Null(selector.PinnedHwnd);
    }

    [Theory]
    [InlineData(true, false, 800, 600, true)]
    [InlineData(false, false, 800, 600, false)]
    [InlineData(true, true, 800, 600, false)]
    [InlineData(true, false, 0, 600, false)]
    [InlineData(true, false, 800, 0, false)]
    public void IsUsableWindow_RejectsHiddenMinimizedOrEmptyWindows(
        bool visible,
        bool minimized,
        int width,
        int height,
        bool expected)
    {
        Assert.Equal(
            expected,
            BrowserWindowTracker.IsUsableWindow(visible, minimized, new Rectangle(0, 0, width, height)));
    }
}
