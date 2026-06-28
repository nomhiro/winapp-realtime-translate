using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using RealtimeTranslate.Config;
using RealtimeTranslate.ViewModels;

namespace RealtimeTranslate;

/// <summary>専用ウィンドウ。ViewModel を生成・保持し、ツールバー操作を ViewModel に委譲する。</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel(new SettingsStore());
        DataContext = _viewModel;

        Loaded += OnLoaded;
        Closing += OnClosing;

        _viewModel.Subtitles.CollectionChanged += OnSubtitlesChanged;
        _viewModel.ScreenTexts.CollectionChanged += OnScreenTextsChanged;
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

    private void OnFontPlus(object sender, RoutedEventArgs e) => _viewModel.AdjustFontSize(2);

    private void OnFontMinus(object sender, RoutedEventArgs e) => _viewModel.AdjustFontSize(-2);

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _viewModel.OpenSettings(this);

    private void OnSubtitlesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && SubtitleList.Items.Count > 0)
        {
            SubtitleList.ScrollIntoView(SubtitleList.Items[^1]);
        }
    }

    private void OnScreenTextsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && ScreenList.Items.Count > 0)
        {
            ScreenList.ScrollIntoView(ScreenList.Items[^1]);
        }
    }
}
