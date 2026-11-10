using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using RealtimeTranslate.Config;
using RealtimeTranslate.Models;
using RealtimeTranslate.ViewModels;

namespace RealtimeTranslate;

/// <summary>専用ウィンドウ。ViewModel を生成・保持し、ツールバー操作を ViewModel に委譲する。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _copyToClipboard;
    private readonly HistoryFollowState _subtitleFollowState = new();
    private readonly HistoryFollowState _screenFollowState = new();
    private readonly HashSet<ListBox> _pendingHistoryScrolls = [];
    private readonly Dictionary<ListBox, HistoryReadingPosition> _pendingReadingPositions = [];
    private ScrollViewer? _subtitleScrollViewer;
    private ScrollViewer? _screenScrollViewer;

    public MainWindow() : this(new SettingsStore(), Clipboard.SetText, initializeOnLoad: true)
    {
    }

    internal MainWindow(SettingsStore settingsStore, Action<string> copyToClipboard, bool initializeOnLoad)
    {
        InitializeComponent();

        _viewModel = new MainViewModel(settingsStore);
        _copyToClipboard = copyToClipboard;
        DataContext = _viewModel;

        if (initializeOnLoad)
        {
            Loaded += OnLoaded;
        }
        Closing += OnClosing;

        _viewModel.Subtitles.CollectionChanged += OnSubtitlesChanged;
        _viewModel.ScreenTexts.CollectionChanged += OnScreenTextsChanged;
        UpdateLatestButton(SubtitleLatestButton, _subtitleFollowState);
        UpdateLatestButton(ScreenLatestButton, _screenFollowState);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _viewModel.Initialize();

    private void OnClosing(object? sender, CancelEventArgs e) => _viewModel.Dispose();

    private async void OnToggleAudio(object sender, RoutedEventArgs e)
        => await _viewModel.ToggleAudioAsync();

    private void OnToggleMode(object sender, RoutedEventArgs e)
    {
        ScreenCaptureMode next = _viewModel.IsContinuous
            ? ScreenCaptureMode.OnDemand
            : ScreenCaptureMode.Continuous;
        _viewModel.SetScreenMode(next);
    }

    private async void OnTranslateNow(object sender, RoutedEventArgs e)
        => await _viewModel.TriggerScreenCaptureAsync();

    private async void OnAnswerNow(object sender, RoutedEventArgs e)
        => await _viewModel.AnswerNowAsync();

    private void OnFontPlus(object sender, RoutedEventArgs e) => _viewModel.AdjustFontSize(2);

    private void OnFontMinus(object sender, RoutedEventArgs e) => _viewModel.AdjustFontSize(-2);

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _viewModel.OpenSettings(this);

    private void OnOpenUsage(object sender, RoutedEventArgs e) => _viewModel.OpenUsage(this);

    private void OnClearHistory(object sender, RoutedEventArgs e) => _viewModel.ClearHistory();

    private void OnSubtitleLatest(object sender, RoutedEventArgs e)
        => ReturnToLatest(SubtitleList, SubtitleLatestButton, _subtitleFollowState);

    private void OnScreenLatest(object sender, RoutedEventArgs e)
        => ReturnToLatest(ScreenList, ScreenLatestButton, _screenFollowState);

    private void OnSubtitleScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateFollowState(SubtitleList, e, SubtitleLatestButton, _subtitleFollowState, ref _subtitleScrollViewer);

    private void OnScreenScrollChanged(object sender, ScrollChangedEventArgs e)
        => UpdateFollowState(ScreenList, e, ScreenLatestButton, _screenFollowState, ref _screenScrollViewer);

    private void OnHistoryPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ListBox list)
        {
            _pendingReadingPositions.Remove(list);
            if (e.Delta > 0)
            {
                PauseFollowingForUserScroll(list);
            }
        }
    }

    private void OnHistoryPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is ListBox list && e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            _pendingReadingPositions.Remove(list);
            if (e.Key is Key.Up or Key.PageUp or Key.Home)
            {
                PauseFollowingForUserScroll(list);
            }
        }
    }

    private void OnHistoryScrollBarScroll(object sender, ScrollEventArgs e)
    {
        if (sender is not ListBox list || e.OriginalSource is not ScrollBar { Orientation: Orientation.Vertical })
        {
            return;
        }

        _pendingReadingPositions.Remove(list);
        ScrollViewer? scrollViewer = list == SubtitleList ? _subtitleScrollViewer : _screenScrollViewer;
        if (scrollViewer is not null
            && e.NewValue < scrollViewer.ScrollableHeight - HistoryFollowState.BottomTolerance)
        {
            PauseFollowingForUserScroll(list);
        }
    }

    private void PauseFollowingForUserScroll(ListBox list)
    {
        ScrollViewer? scrollViewer = list == SubtitleList ? _subtitleScrollViewer : _screenScrollViewer;
        if (scrollViewer is null || scrollViewer.VerticalOffset <= 0)
        {
            return;
        }

        HistoryFollowState state = list == SubtitleList ? _subtitleFollowState : _screenFollowState;
        Button button = list == SubtitleList ? SubtitleLatestButton : ScreenLatestButton;
        state.Pause();
        UpdateLatestButton(button, state);
    }

    private void OnCopyHistoryText(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { CommandParameter: string text } || text.Length == 0)
        {
            return;
        }

        try
        {
            _copyToClipboard(text);
        }
        catch (ExternalException)
        {
            MessageBox.Show(this, "クリップボードにコピーできませんでした。", "コピー失敗",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSubtitlesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HandleCollectionChange(e, _observedSubtitles, OnSubtitleEntryChanged);
        HandleHistoryScrollChange(e, _viewModel.Subtitles.Count, SubtitleList, SubtitleLatestButton,
            _subtitleScrollViewer, _subtitleFollowState);
    }

    private void OnScreenTextsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HandleCollectionChange(e, _observedScreenTexts, OnScreenEntryChanged);
        HandleHistoryScrollChange(e, _viewModel.ScreenTexts.Count, ScreenList, ScreenLatestButton,
            _screenScrollViewer, _screenFollowState);
    }

    private void OnSubtitleEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_subtitleFollowState.IsFollowing)
        {
            ScrollToEnd(SubtitleList, _subtitleFollowState);
        }
    }

    private void OnScreenEntryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_screenFollowState.IsFollowing)
        {
            ScrollToEnd(ScreenList, _screenFollowState);
        }
    }

    private void UpdateFollowState(
        ListBox list,
        ScrollChangedEventArgs e,
        Button latestButton,
        HistoryFollowState followState,
        ref ScrollViewer? historyScrollViewer)
    {
        if ((e.OriginalSource as ScrollViewer ?? e.Source as ScrollViewer) is not ScrollViewer scrollViewer)
        {
            return;
        }

        historyScrollViewer = scrollViewer;
        // Extent estimates can change as virtualized items are measured.
        double previousOffset = e.VerticalOffset - e.VerticalChange;
        double maximumUnchangedOffset = Math.Min(previousOffset, scrollViewer.ScrollableHeight);
        double layoutChange = Math.Abs(e.ExtentHeightChange) + Math.Abs(e.ViewportHeightChange);
        bool layoutChanged = layoutChange > HistoryFollowState.BottomTolerance;
        bool movedUp = e.VerticalOffset < maximumUnchangedOffset - HistoryFollowState.BottomTolerance;
        if (movedUp && (!layoutChanged || followState.IsFollowing
            || (_pendingReadingPositions.ContainsKey(list)
                && Math.Abs(e.VerticalChange) > layoutChange + HistoryFollowState.BottomTolerance)))
        {
            _pendingReadingPositions.Remove(list);
            followState.Update(scrollViewer.VerticalOffset, scrollViewer.ViewportHeight, scrollViewer.ExtentHeight);
            UpdateLatestButton(latestButton, followState);
            return;
        }

        if (layoutChanged)
        {
            if (followState.IsFollowing)
            {
                ScrollToEnd(list, followState);
            }
            else if (scrollViewer.ScrollableHeight - scrollViewer.VerticalOffset <= HistoryFollowState.BottomTolerance)
            {
                followState.Update(scrollViewer.VerticalOffset, scrollViewer.ViewportHeight, scrollViewer.ExtentHeight);
                UpdateLatestButton(latestButton, followState);
            }
            return;
        }

        if (e.VerticalChange == 0)
        {
            return;
        }

        _pendingReadingPositions.Remove(list);
        followState.Update(scrollViewer.VerticalOffset, scrollViewer.ViewportHeight, scrollViewer.ExtentHeight);
        UpdateLatestButton(latestButton, followState);
    }

    private void ReturnToLatest(ListBox list, Button latestButton, HistoryFollowState followState)
    {
        _pendingReadingPositions.Remove(list);
        followState.ReturnToLatest();
        UpdateLatestButton(latestButton, followState);
        ScrollToEnd(list, followState);
    }

    private static void UpdateLatestButton(Button button, HistoryFollowState followState)
        => button.Visibility = followState.IsFollowing ? Visibility.Collapsed : Visibility.Visible;

    private void HandleHistoryScrollChange(
        NotifyCollectionChangedEventArgs e,
        int itemCount,
        ListBox list,
        Button latestButton,
        ScrollViewer? scrollViewer,
        HistoryFollowState followState)
    {
        if (itemCount == 0)
        {
            _pendingReadingPositions.Remove(list);
            followState.ReturnToLatest();
            UpdateLatestButton(latestButton, followState);
        }
        else if (e.Action == NotifyCollectionChangedAction.Add && followState.IsFollowing)
        {
            ScrollToEnd(list, followState);
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && !followState.IsFollowing
            && scrollViewer is not null && e.OldStartingIndex >= 0
            && !_pendingReadingPositions.ContainsKey(list))
        {
            for (int i = 0; i < list.Items.Count; i++)
            {
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                {
                    continue;
                }

                double top = container.TranslatePoint(new Point(), scrollViewer).Y;
                if (top + container.ActualHeight <= 0 || top >= scrollViewer.ViewportHeight)
                {
                    continue;
                }

                if (container.DataContext is not object item)
                {
                    continue;
                }
                int itemIndex = list.Items.IndexOf(item);
                if (itemIndex < 0)
                {
                    continue;
                }
                if (e.OldStartingIndex > itemIndex)
                {
                    return;
                }

                // Preserve a visible item, rather than an estimated pixel offset, when old entries expire.
                _pendingReadingPositions[list] = new HistoryReadingPosition(item, top);
                list.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (!_pendingReadingPositions.Remove(list, out HistoryReadingPosition? position)
                        || followState.IsFollowing || !list.Items.Contains(position.Item))
                    {
                        return;
                    }

                    var anchor = list.ItemContainerGenerator.ContainerFromItem(position.Item) as FrameworkElement;
                    if (anchor is null)
                    {
                        list.ScrollIntoView(position.Item);
                        list.UpdateLayout();
                        anchor = list.ItemContainerGenerator.ContainerFromItem(position.Item) as FrameworkElement;
                    }

                    if (anchor is not null)
                    {
                        double currentTop = anchor.TranslatePoint(new Point(), scrollViewer).Y;
                        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset + currentTop - position.Top);
                    }
                });
                break;
            }
        }
    }

    private sealed record HistoryReadingPosition(object Item, double Top);

    private static void HandleCollectionChange<T>(
        NotifyCollectionChangedEventArgs e,
        HashSet<T> observedItems,
        PropertyChangedEventHandler handler)
        where T : class, INotifyPropertyChanged
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (T item in observedItems)
            {
                item.PropertyChanged -= handler;
            }
            observedItems.Clear();
            return;
        }

        if (e.OldItems is not null)
        {
            foreach (T item in e.OldItems)
            {
                item.PropertyChanged -= handler;
                observedItems.Remove(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (T item in e.NewItems)
            {
                item.PropertyChanged += handler;
                observedItems.Add(item);
            }
        }
    }

    private readonly HashSet<SubtitleEntry> _observedSubtitles = [];
    private readonly HashSet<ScreenTextEntry> _observedScreenTexts = [];

    // レイアウト確定後にスクロールし、仮想化パネルのコンテナ生成と競合させない。
    private void ScrollToEnd(ListBox list, HistoryFollowState followState)
    {
        if (!_pendingHistoryScrolls.Add(list))
        {
            return;
        }

        list.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _pendingHistoryScrolls.Remove(list);
            if (followState.IsFollowing && list.Items.Count > 0)
            {
                ScrollViewer? scrollViewer = list == SubtitleList ? _subtitleScrollViewer : _screenScrollViewer;
                if (scrollViewer is not null)
                {
                    scrollViewer.ScrollToEnd();
                }
                else
                {
                    list.ScrollIntoView(list.Items[^1]);
                }
            }
        });
    }
}
