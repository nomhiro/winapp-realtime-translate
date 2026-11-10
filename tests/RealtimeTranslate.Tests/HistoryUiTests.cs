using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RealtimeTranslate.Config;
using RealtimeTranslate.Models;
using RealtimeTranslate.ViewModels;
using RealtimeTranslate.Views;

namespace RealtimeTranslate.Tests;

public sealed class HistoryUiTests : IClassFixture<HistoryUiFixture>
{
    private readonly HistoryUiFixture _fixture;

    public HistoryUiTests(HistoryUiFixture fixture) => _fixture = fixture;

    [Fact]
    public void HistoriesFollowIndependentlyAndReturnToLatest()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 50; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"subtitle {i}", $"字幕 {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"screen {i}", $"画面 {i}"));
            }
            h.Flush();

            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);

            h.SubtitleScroll.ScrollToTop();
            h.Flush();
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);

            double subtitleOffset = h.SubtitleScroll.VerticalOffset;
            h.ViewModel.Subtitles.Add(new SubtitleEntry("new subtitle", "新しい字幕"));
            h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "new screen", "新しい画面"));
            h.Flush();

            Assert.Equal(subtitleOffset, h.SubtitleScroll.VerticalOffset);
            AssertAtBottom(h.ScreenScroll);

            h.SubtitleLatest.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            h.Flush();
            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
            AssertAtBottom(h.SubtitleScroll);

            h.ScreenScroll.ScrollToTop();
            h.Flush();
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);

            double screenOffset = h.ScreenScroll.VerticalOffset;
            h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "another screen", "別の画面"));
            h.ViewModel.Subtitles.Add(new SubtitleEntry("another subtitle", "別の字幕"));
            h.Flush();
            Assert.Equal(screenOffset, h.ScreenScroll.VerticalOffset);
            AssertAtBottom(h.SubtitleScroll);

            h.ScreenLatest.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            h.Flush();
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);
            AssertAtBottom(h.ScreenScroll);
        }));
    }

    [Fact]
    public void ScrollingUpOneItemStopsFollowing()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 30; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);

            h.SubtitleScroll.LineUp();
            h.ScreenScroll.LineUp();
            h.Flush();
            Assert.True(h.SubtitleScroll.VerticalOffset < h.SubtitleScroll.ScrollableHeight);
            Assert.True(h.ScreenScroll.VerticalOffset < h.ScreenScroll.ScrollableHeight);
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            double offset = h.SubtitleScroll.VerticalOffset;
            double screenOffset = h.ScreenScroll.VerticalOffset;
            h.ViewModel.Subtitles.Add(new SubtitleEntry("new", "new"));
            h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "new", "new"));
            h.Flush();
            Assert.Equal(offset, h.SubtitleScroll.VerticalOffset);
            Assert.Equal(screenOffset, h.ScreenScroll.VerticalOffset);
        }));
    }

    [Fact]
    public void StreamingChangesFollowOnlyWhileAtLatest()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 30; i++)
            {
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            var last = h.ViewModel.ScreenTexts[^1];

            last.Translation = string.Join(" ", Enumerable.Repeat("streamed translation", 100));
            h.Flush();
            AssertAtBottom(h.ScreenScroll);

            h.ScreenScroll.ScrollToTop();
            h.Flush();
            double offset = h.ScreenScroll.VerticalOffset;
            last.Translation += string.Join(" ", Enumerable.Repeat(" more", 100));
            h.Flush();
            Assert.Equal(offset, h.ScreenScroll.VerticalOffset);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            h.ScreenLatest.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            h.Flush();
            AssertAtBottom(h.ScreenScroll);
        }));
    }

    [Fact]
    public void VariableHeightHistoryCanStopAndResumeFollowing()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 80; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat($"translation {i}", i % 3 == 0 ? 25 : 2));
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
            }
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);

            h.SubtitleScroll.ScrollToVerticalOffset(h.SubtitleScroll.ScrollableHeight / 2);
            h.ScreenScroll.ScrollToVerticalOffset(h.ScreenScroll.ScrollableHeight / 2);
            h.Flush();
            Assert.True(h.SubtitleScroll.VerticalOffset < h.SubtitleScroll.ScrollableHeight);
            Assert.True(h.ScreenScroll.VerticalOffset < h.ScreenScroll.ScrollableHeight);
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            h.SubtitleScroll.ScrollToEnd();
            h.ScreenScroll.ScrollToEnd();
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);

            h.ViewModel.Subtitles.Add(new SubtitleEntry("new", "new"));
            h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "new", "new"));
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
        }));
    }

    [Fact]
    public void UsageButtonOpensSeparateWindowWithCurrentSessionStats()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            h.ViewModel.ScreenTranslationUsage.OnRequestStarted(23);
            h.ViewModel.ScreenTranslationUsage.OnRequestCompleted(
                RealtimeTranslate.Translation.TranslationRequestOutcome.Succeeded);
            h.ViewModel.ScreenTranslationUsage.OnModelRequestStarted();
            h.ViewModel.ScreenTranslationUsage.OnModelTokensUsed(
                RealtimeTranslate.Config.AzureOpenAIModelProfile.Gpt41,
                100,
                20);
            h.ViewModel.ScreenTranslationUsage.OnModelRequestCompleted(
                RealtimeTranslate.Translation.TranslationRequestOutcome.Succeeded);

            var openButton = (Button)h.Window.FindName("OpenUsageButton");
            openButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            h.Flush();

            UsageWindow usageWindow = Assert.Single(h.Window.OwnedWindows.OfType<UsageWindow>());
            Assert.True(usageWindow.IsVisible);
            Assert.Same(h.ViewModel, usageWindow.DataContext);
            Assert.Equal("1", ((TextBlock)usageWindow.FindName("TotalRequestsText")).Text);
            Assert.Equal("100", ((TextBlock)usageWindow.FindName("ModelInputTokensText")).Text);
            Assert.StartsWith("US$ ", ((TextBlock)usageWindow.FindName("EstimatedTotalCostText")).Text);
            usageWindow.Close();
        }));
    }

    [Fact]
    public void ScrollingUpTakesPriorityOverPendingStreamingUpdate()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 50; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat($"translation {i}", i % 3 == 0 ? 25 : 2));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
            }
            h.Flush();
            AssertAtBottom(h.ScreenScroll);

            h.ViewModel.ScreenTexts[^1].Translation += string.Join(" ", Enumerable.Repeat(" streaming", 100));
            h.ScreenScroll.ScrollToTop();
            h.Flush();

            Assert.Equal(0, h.ScreenScroll.VerticalOffset);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
        }));
    }

    [Theory]
    [InlineData("mouse", true)]
    [InlineData("keyboard", true)]
    [InlineData("scrollbar", true)]
    [InlineData("mouse", false)]
    [InlineData("keyboard", false)]
    [InlineData("scrollbar", false)]
    public void UserScrollUpTakesPriorityOverPendingStreamingUpdate(string input, bool subtitle)
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 50; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat($"translation {i}", i % 3 == 0 ? 25 : 2));
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
            }
            h.Flush();
            ListBox list = subtitle ? h.SubtitleList : h.ScreenList;
            ScrollViewer scroll = subtitle ? h.SubtitleScroll : h.ScreenScroll;
            Button latest = subtitle ? h.SubtitleLatest : h.ScreenLatest;
            AssertAtBottom(scroll);

            if (subtitle)
            {
                h.ViewModel.Subtitles[^1].Translation += string.Join(" ", Enumerable.Repeat(" streaming", 100));
            }
            else
            {
                h.ViewModel.ScreenTexts[^1].Translation += string.Join(" ", Enumerable.Repeat(" streaming", 100));
            }

            switch (input)
            {
                case "mouse":
                    list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120)
                    {
                        RoutedEvent = Mouse.PreviewMouseWheelEvent,
                    });
                    break;
                case "keyboard":
                    var source = Assert.IsAssignableFrom<PresentationSource>(PresentationSource.FromVisual(h.Window));
                    list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.PageUp)
                    {
                        RoutedEvent = Keyboard.PreviewKeyDownEvent,
                    });
                    break;
                case "scrollbar":
                    var bar = FindVisual<ScrollBar>(list,
                        element => element.Orientation == Orientation.Vertical && element.IsVisible);
                    bar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, scroll.VerticalOffset - 10)
                    {
                        RoutedEvent = ScrollBar.ScrollEvent,
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(input));
            }
            scroll.LineUp();
            h.Flush();

            Assert.True(scroll.VerticalOffset < scroll.ScrollableHeight);
            Assert.Equal(Visibility.Visible, latest.Visibility);
        }));
    }

    [Fact]
    public void FollowingSurvivesCappedAddsAndWindowResizing()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 200; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat($"translation {i}", i % 5 == 0 ? 20 : 1));
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                if (i < 100)
                {
                    h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
                }
            }
            h.Flush();

            for (int i = 0; i < 10; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat("long translation", i % 2 == 0 ? 30 : 1));
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                h.ViewModel.Subtitles.RemoveAt(0);
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
                h.ViewModel.ScreenTexts.RemoveAt(0);
                h.Flush();
                AssertAtBottom(h.SubtitleScroll);
                AssertAtBottom(h.ScreenScroll);
                Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
                Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);
            }

            h.Window.Width = 360;
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
            h.Window.Width = 700;
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
        }));
    }

    [Theory]
    [InlineData(600)]
    [InlineData(768)]
    [InlineData(788)]
    [InlineData(1200)]
    public void ExpandingToShowTheEntireHistoryResumesFollowing(double maximumWindowHeight)
    {
        _fixture.Run(() => WithWindow(h =>
        {
            h.Window.MaxHeight = maximumWindowHeight;
            h.Window.Height = h.Window.MinHeight;
            for (int i = 0; i < 4; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            double initialWindowHeight = h.Window.ActualHeight;
            Assert.True(h.SubtitleScroll.ScrollableHeight > 0);
            Assert.True(h.ScreenScroll.ScrollableHeight > 0);
            h.SubtitleScroll.ScrollToTop();
            h.ScreenScroll.ScrollToTop();
            h.Flush();
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            h.ViewModel.FontSize = 10;
            h.ViewModel.ShowOriginal = false;
            h.Window.Height = 1200;
            h.Flush();
            Assert.True(h.Window.ActualHeight > initialWindowHeight);
            Assert.Equal(0, h.SubtitleScroll.ScrollableHeight);
            Assert.Equal(0, h.ScreenScroll.ScrollableHeight);
            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);
        }));
    }

    [Fact]
    public void ExpandingWhileHistoryStillOverflowsKeepsFollowingPaused()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            h.Window.MaxHeight = 768;
            h.Window.Height = h.Window.MinHeight;
            for (int i = 0; i < 20; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            h.SubtitleScroll.ScrollToTop();
            h.ScreenScroll.ScrollToTop();
            h.Flush();
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            h.ViewModel.FontSize = 10;
            h.ViewModel.ShowOriginal = false;
            h.Window.Height = 1200;
            h.Flush();
            Assert.True(h.SubtitleScroll.ScrollableHeight > 0);
            Assert.True(h.ScreenScroll.ScrollableHeight > 0);
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
        }));
    }

    [Fact]
    public void MaximumHistoryKeepsReadingPositionWhenOldItemsAreRemoved()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 200; i++)
            {
                string text = string.Join(" ", Enumerable.Repeat($"translation {i}", i % 5 == 0 ? 20 : 1));
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                if (i < 100)
                {
                    h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
                }
            }
            h.Flush();

            h.SubtitleScroll.ScrollToVerticalOffset(h.SubtitleScroll.ScrollableHeight / 2);
            h.ScreenScroll.ScrollToVerticalOffset(h.ScreenScroll.ScrollableHeight / 2);
            h.Flush();
            var subtitle = FirstVisible(h.SubtitleList, h.SubtitleScroll);
            var screenText = FirstVisible(h.ScreenList, h.ScreenScroll);
            Assert.True(h.ViewModel.Subtitles.IndexOf((SubtitleEntry)subtitle) >= 4);
            Assert.True(h.ViewModel.ScreenTexts.IndexOf((ScreenTextEntry)screenText) >= 4);
            double subtitleTop = ItemTop(h.SubtitleList, subtitle, h.SubtitleScroll);
            double screenTop = ItemTop(h.ScreenList, screenText, h.ScreenScroll);

            for (int i = 0; i < 4; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry("new", "new"));
                h.ViewModel.Subtitles.RemoveAt(0);
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "new", "new"));
                h.ViewModel.ScreenTexts.RemoveAt(0);
            }
            h.Flush();

            Assert.Equal(200, h.ViewModel.Subtitles.Count);
            Assert.Equal(100, h.ViewModel.ScreenTexts.Count);
            Assert.InRange(Math.Abs(ItemTop(h.SubtitleList, subtitle, h.SubtitleScroll) - subtitleTop), 0, 1);
            Assert.InRange(Math.Abs(ItemTop(h.ScreenList, screenText, h.ScreenScroll) - screenTop), 0, 1);
        }));
    }

    [Fact]
    public void ReadingAtTheTopStaysThereWhenOldEntriesExpire()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 30; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            h.SubtitleScroll.ScrollToTop();
            h.ScreenScroll.ScrollToTop();
            h.Flush();

            for (int i = 0; i < 4; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry("new", "new"));
                h.ViewModel.Subtitles.RemoveAt(0);
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", "new", "new"));
                h.ViewModel.ScreenTexts.RemoveAt(0);
            }
            h.Flush();

            Assert.Equal(0, h.SubtitleScroll.VerticalOffset);
            Assert.Equal(0, h.ScreenScroll.VerticalOffset);
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
        }));
    }

    [Fact]
    public void ClearingAtTopResetsFollowingForBothHistories()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 30; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", $"translation {i}"));
            }
            h.Flush();
            h.SubtitleScroll.ScrollToTop();
            h.ScreenScroll.ScrollToTop();
            h.Flush();
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
            h.ViewModel.ClearHistory();
            h.Flush();

            Assert.Equal(Visibility.Collapsed, h.SubtitleLatest.Visibility);
            Assert.Equal(Visibility.Collapsed, h.ScreenLatest.Visibility);
            for (int i = 0; i < 30; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"new {i}", $"new {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"new {i}", $"new {i}"));
            }
            h.Flush();
            AssertAtBottom(h.SubtitleScroll);
            AssertAtBottom(h.ScreenScroll);
        }));
    }

    [Fact]
    public void NewScrollCancelsPendingPositionAdjustment()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 80; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", $"translation {i}"));
            }
            h.Flush();
            h.SubtitleScroll.ScrollToVerticalOffset(h.SubtitleScroll.ScrollableHeight / 2);
            h.Flush();

            double targetOffset = h.SubtitleScroll.ScrollableHeight / 4;
            h.ViewModel.Subtitles.Add(new SubtitleEntry("new", "new"));
            h.ViewModel.Subtitles.RemoveAt(0);
            h.SubtitleScroll.ScrollToVerticalOffset(targetOffset);
            h.Flush();
            Assert.InRange(Math.Abs(targetOffset - h.SubtitleScroll.VerticalOffset), 0, 0.01);
            Assert.Equal(Visibility.Visible, h.SubtitleLatest.Visibility);
        }));
    }

    [Fact]
    public void CopyMenusUseTheCurrentItemAndTranslation()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            for (int i = 0; i < 40; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"subtitle original {i}", $"subtitle translation {i}"));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry(
                    "browser", $"screen original {i}", $"screen translation {i}"));
            }
            h.ViewModel.ShowOriginal = false;
            h.Flush();

            var subtitle = h.ViewModel.Subtitles[5];
            h.SubtitleList.ScrollIntoView(subtitle);
            h.Flush();
            CopyBoth(h, h.SubtitleList, subtitle, subtitle.Original, subtitle.Translation);

            var screen = h.ViewModel.ScreenTexts[5];
            h.ScreenList.ScrollIntoView(screen);
            h.Flush();
            screen.Translation = "updated screen translation";
            h.Flush();
            CopyBoth(h, h.ScreenList, screen, screen.Original, screen.Translation);

            subtitle = h.ViewModel.Subtitles[35];
            h.SubtitleList.ScrollIntoView(subtitle);
            h.Flush();
            CopyBoth(h, h.SubtitleList, subtitle, subtitle.Original, subtitle.Translation);

            screen = h.ViewModel.ScreenTexts[35];
            h.ScreenList.ScrollIntoView(screen);
            h.Flush();
            CopyBoth(h, h.ScreenList, screen, screen.Original, screen.Translation);
        }));
    }

    [Fact]
    public void MeaningfulBreaksSurviveStreamingResizeAndCopy()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            const string original = "No. 34 What does the man offer to do?\n(A) Provide a discount\n(B) Issue a refund";
            const string translation = "34番 男性は何を提案しますか？\n(A) 割引を提供する\n(B) 返金する\n\n次の設問。";
            var screen = new ScreenTextEntry("browser", original, "");
            var subtitle = new SubtitleEntry("A short caption.", "短い字幕です。");
            h.ViewModel.ScreenTexts.Add(screen);
            h.ViewModel.Subtitles.Add(subtitle);

            foreach (string delta in new[] { translation[..12], translation[12..] })
            {
                screen.Translation += delta;
                h.Flush();
                Assert.Equal(screen.Translation, ItemText(h.ScreenList, screen, screen.Translation).Text);
            }

            foreach (double width in new[] { 360d, 800d })
            {
                h.Window.Width = width;
                h.Flush();
                Assert.Equal(translation, ItemText(h.ScreenList, screen, translation).Text);
                CopyBoth(h, h.ScreenList, screen, original, translation);
                CopyBoth(h, h.SubtitleList, subtitle, subtitle.Original, subtitle.Translation);
            }
        }));
    }

    [Fact]
    public void EmptyCopyOptionsAreDisabledUntilTextIsAvailable()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            var subtitle = new SubtitleEntry(string.Empty, string.Empty);
            var screen = new ScreenTextEntry("browser", string.Empty, string.Empty);
            h.ViewModel.Subtitles.Add(subtitle);
            h.ViewModel.ScreenTexts.Add(screen);
            h.Flush();

            ContextMenu subtitleMenu = ItemMenu(h.SubtitleList, subtitle);
            ContextMenu screenMenu = ItemMenu(h.ScreenList, screen);
            h.Flush();
            Assert.False(Assert.IsType<MenuItem>(subtitleMenu.Items[0]).IsEnabled);
            Assert.False(Assert.IsType<MenuItem>(subtitleMenu.Items[1]).IsEnabled);
            Assert.False(Assert.IsType<MenuItem>(screenMenu.Items[0]).IsEnabled);
            Assert.False(Assert.IsType<MenuItem>(screenMenu.Items[1]).IsEnabled);

            subtitle.Original = "original";
            subtitle.Translation = "translation";
            screen.Translation = "translation";
            h.Flush();
            Assert.True(Assert.IsType<MenuItem>(subtitleMenu.Items[0]).IsEnabled);
            Assert.True(Assert.IsType<MenuItem>(subtitleMenu.Items[1]).IsEnabled);
            Assert.True(Assert.IsType<MenuItem>(screenMenu.Items[1]).IsEnabled);
        }));
    }

    [Fact]
    public void LongTextWrapsAfterResizeAndHistoryIsVirtualized()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            string text = string.Join(" ", Enumerable.Repeat("long translation content", 30));
            for (int i = 0; i < 100; i++)
            {
                h.ViewModel.Subtitles.Add(new SubtitleEntry($"original {i}", text));
                h.ViewModel.ScreenTexts.Add(new ScreenTextEntry("browser", $"original {i}", text));
            }
            h.Flush();
            h.SubtitleList.ScrollIntoView(h.ViewModel.Subtitles[0]);
            h.ScreenList.ScrollIntoView(h.ViewModel.ScreenTexts[0]);
            h.Flush();

            AssertVirtualized(h.SubtitleList);
            AssertVirtualized(h.ScreenList);
            var subtitleText = ItemText(h.SubtitleList, h.ViewModel.Subtitles[0], text);
            var screenText = ItemText(h.ScreenList, h.ViewModel.ScreenTexts[0], text);
            double subtitleHeight = subtitleText.ActualHeight;
            double screenHeight = screenText.ActualHeight;
            Assert.True(subtitleHeight > subtitleText.FontSize * 2);
            Assert.True(screenHeight > screenText.FontSize * 2);

            h.Window.Width = 360;
            h.Flush();
            Assert.True(subtitleText.ActualHeight > subtitleHeight);
            Assert.True(screenText.ActualHeight > screenHeight);
            Assert.True(subtitleText.ActualWidth <= h.SubtitleScroll.ViewportWidth + 1);
            Assert.True(screenText.ActualWidth <= h.ScreenScroll.ViewportWidth + 1);
            AssertVirtualized(h.SubtitleList);
            AssertVirtualized(h.ScreenList);
        }));
    }

    [Fact]
    public void OneLongEntryCanBeScrolledFromBeginningToEnd()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            string text = string.Join("\n", Enumerable.Range(0, 80).Select(i => $"translation line {i}"));
            var subtitle = new SubtitleEntry("original", text);
            var screen = new ScreenTextEntry("browser", "original", text);
            h.ViewModel.Subtitles.Add(subtitle);
            h.ViewModel.ScreenTexts.Add(screen);
            h.Flush();

            Assert.True(h.SubtitleScroll.ScrollableHeight > h.SubtitleScroll.ViewportHeight);
            Assert.True(h.ScreenScroll.ScrollableHeight > h.ScreenScroll.ViewportHeight);
            h.SubtitleScroll.ScrollToTop();
            h.ScreenScroll.ScrollToTop();
            h.Flush();
            double subtitleTop = ItemTop(h.SubtitleList, subtitle, h.SubtitleScroll);
            double screenTop = ItemTop(h.ScreenList, screen, h.ScreenScroll);
            h.SubtitleScroll.ScrollToEnd();
            h.ScreenScroll.ScrollToEnd();
            h.Flush();
            Assert.True(ItemTop(h.SubtitleList, subtitle, h.SubtitleScroll) < subtitleTop - h.SubtitleScroll.ViewportHeight);
            Assert.True(ItemTop(h.ScreenList, screen, h.ScreenScroll) < screenTop - h.ScreenScroll.ViewportHeight);
        }));
    }

    [Fact]
    public void ScrollingWithinOneLongEntryStopsFollowingStreamingChanges()
    {
        _fixture.Run(() => WithWindow(h =>
        {
            string text = string.Join("\n", Enumerable.Range(0, 80).Select(i => $"translation line {i}"));
            var screen = new ScreenTextEntry("browser", "original", text);
            h.ViewModel.ScreenTexts.Add(screen);
            h.Flush();
            AssertAtBottom(h.ScreenScroll);

            h.ScreenScroll.LineUp();
            h.Flush();
            double offset = h.ScreenScroll.VerticalOffset;
            Assert.True(offset < h.ScreenScroll.ScrollableHeight);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);

            screen.Translation += "\nmore streamed text";
            h.Flush();
            Assert.Equal(offset, h.ScreenScroll.VerticalOffset, 2);
            Assert.Equal(Visibility.Visible, h.ScreenLatest.Visibility);
        }));
    }

    private static void CopyBoth(WindowHarness h, ListBox list, object entry, string original, string translation)
    {
        ContextMenu menu = ItemMenu(list, entry);
        h.Flush();

        var originalItem = Assert.IsType<MenuItem>(menu.Items[0]);
        var translationItem = Assert.IsType<MenuItem>(menu.Items[1]);
        Assert.Equal(original, originalItem.CommandParameter);
        Assert.Equal(translation, translationItem.CommandParameter);
        originalItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        translationItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(new[] { original, translation }, h.Copied.TakeLast(2));
    }

    private static ContextMenu ItemMenu(ListBox list, object entry)
    {
        var item = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(entry));
        var target = FindVisual<FrameworkElement>(item, element => element.ContextMenu is not null);
        ContextMenu menu = Assert.IsType<ContextMenu>(target.ContextMenu);
        menu.PlacementTarget = target;
        return menu;
    }

    private static double ItemTop(ListBox list, object entry, ScrollViewer scroll)
    {
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(entry));
        return container.TransformToAncestor(scroll).Transform(new Point(0, 0)).Y;
    }

    private static object FirstVisible(ListBox list, ScrollViewer scroll)
    {
        foreach (object entry in list.Items)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(entry) is ListBoxItem container
                && ItemTop(list, entry, scroll) + container.ActualHeight > 0)
            {
                return entry;
            }
        }

        throw new InvalidOperationException("Could not find a visible history item.");
    }

    private static TextBlock ItemText(ListBox list, object entry, string text)
    {
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromItem(entry));
        return FindVisual<TextBlock>(container, block => block.Text == text);
    }

    private static void AssertAtBottom(ScrollViewer scroll)
    {
        Assert.InRange(scroll.ScrollableHeight - scroll.VerticalOffset, 0, 0.01);
    }

    private static void AssertVirtualized(ListBox list)
    {
        Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
        Assert.Equal(VirtualizationMode.Standard, VirtualizingPanel.GetVirtualizationMode(list));
        var panel = FindVisual<VirtualizingStackPanel>(list);
        Assert.True(panel.Children.Count < list.Items.Count / 2);
    }

    private static T FindVisual<T>(DependencyObject parent, Func<T, bool>? predicate = null)
        where T : DependencyObject
        => FindVisualOrNull(parent, predicate)
            ?? throw new InvalidOperationException($"Could not find {typeof(T).Name} in visual tree.");

    private static T? FindVisualOrNull<T>(DependencyObject parent, Func<T, bool>? predicate)
        where T : DependencyObject
    {
        if (parent is T match && (predicate is null || predicate(match)))
        {
            return match;
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            T? result = FindVisualOrNull(VisualTreeHelper.GetChild(parent, i), predicate);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static void WithWindow(Action<WindowHarness> action)
    {
        string settingsPath = Path.Combine(Path.GetTempPath(), $"rt_history_test_{Guid.NewGuid():N}.dat");
        var copied = new List<string>();
        var window = new MainWindow(new SettingsStore(settingsPath), copied.Add, initializeOnLoad: false)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };
        var viewModel = (MainViewModel)window.DataContext;
        viewModel.AlwaysOnTop = false;
        viewModel.WindowOpacity = 0;
        try
        {
            window.Show();
            var harness = new WindowHarness(window, viewModel, copied);
            harness.Flush();
            action(harness);
        }
        finally
        {
            window.Close();
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    private sealed class WindowHarness
    {
        public WindowHarness(MainWindow window, MainViewModel viewModel, List<string> copied)
        {
            Window = window;
            ViewModel = viewModel;
            Copied = copied;
            SubtitleList = (ListBox)window.FindName("SubtitleList");
            ScreenList = (ListBox)window.FindName("ScreenList");
            SubtitleLatest = (Button)window.FindName("SubtitleLatestButton");
            ScreenLatest = (Button)window.FindName("ScreenLatestButton");
            window.UpdateLayout();
            SubtitleScroll = FindVisual<ScrollViewer>(SubtitleList);
            ScreenScroll = FindVisual<ScrollViewer>(ScreenList);
        }

        public MainWindow Window { get; }
        public MainViewModel ViewModel { get; }
        public List<string> Copied { get; }
        public ListBox SubtitleList { get; }
        public ListBox ScreenList { get; }
        public Button SubtitleLatest { get; }
        public Button ScreenLatest { get; }
        public ScrollViewer SubtitleScroll { get; }
        public ScrollViewer ScreenScroll { get; }

        public void Flush()
        {
            Window.UpdateLayout();
            var frame = new DispatcherFrame();
            Window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Window.UpdateLayout();
        }
    }
}

public sealed class HistoryUiFixture : IDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new();
    private readonly Thread _thread;

    public HistoryUiFixture()
    {
        _thread = new Thread(() =>
        {
            try
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                _ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                if (!_ready.TrySetException(ex))
                {
                    throw;
                }
            }
        })
        {
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Task.GetAwaiter().GetResult();
    }

    public void Run(Action action) => _ready.Task.GetAwaiter().GetResult().Invoke(action);

    public void Dispose()
    {
        _ready.Task.GetAwaiter().GetResult().InvokeShutdown();
        if (!_thread.Join(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("WPF test thread did not stop.");
        }
    }
}
