using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using RealtimeTranslate.Audio;
using RealtimeTranslate.Config;
using RealtimeTranslate.Llm;
using RealtimeTranslate.Models;
using RealtimeTranslate.Mvvm;
using RealtimeTranslate.Screen;
using RealtimeTranslate.Translation;
using RealtimeTranslate.Views;

namespace RealtimeTranslate.ViewModels;

/// <summary>
/// アプリの中核。音声パイプライン（ループバック→Speech 翻訳→字幕）と
/// 画面パイプライン（ブラウザ自動検出→OCR→翻訳）を結線し、UI スレッドへマーシャリングする。
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxSubtitles = 200;
    private const int MaxScreenTexts = 100;

    private const string OcrMissingMessage =
        "英語OCRが未インストールです。管理者PowerShellで『Add-WindowsCapability -Online -Name Language.OCR~~~en-US~0.0.1.0』を実行し、アプリを再起動してください。";

    private readonly Dispatcher _dispatcher;
    private readonly SettingsStore _store;
    private readonly LoopbackCaptureService _loopback = new();
    private readonly SpeechTranslationService _speech = new();
    private readonly BrowserWindowTracker _tracker = new();
    private readonly ScreenCaptureService _screenCapture = new();
    private readonly WindowsMediaOcrService _ocr;
    private readonly AutoCaptureScheduler _scheduler;
    private readonly Hotkey.GlobalHotkeyService _hotkey = new();
    private readonly DispatcherTimer _browserTargetTimer;
    private readonly ScreenTranslationUsage _screenTranslationUsage = new();

    private AppSettings _settings;
    private ITextTranslator? _translator;
    private QuestionAnswerService? _answerService;
    private UsageWindow? _usageWindow;

    private SubtitleEntry? _interimSubtitle;
    private string _statusText = "準備完了";
    private bool _isAudioOn;
    private double _fontSize = 18;
    private double _windowOpacity = 0.95;
    private bool _alwaysOnTop = true;
    private bool _showOriginal = true;
    private string _targetBrowserText = "対象: 未検出";
    private BrowserWindowOption _selectedBrowserTarget = BrowserWindowOption.Automatic;

    public MainViewModel(SettingsStore store)
    {
        _dispatcher = Application.Current.Dispatcher;
        _store = store;
        _settings = store.Load();

        _ocr = new WindowsMediaOcrService(_settings.SourceLanguage);
        _scheduler = new AutoCaptureScheduler(_tracker, _screenCapture, _ocr, () => _settings);

        _loopback.PcmAvailable += (_, pcm) => _speech.Write(pcm);
        _loopback.Error += (_, msg) => PostStatus(msg);
        _speech.AudioBytesWritten += _screenTranslationUsage.OnSpeechAudioBytesWritten;
        _speech.SessionStarted += (_, _) => _screenTranslationUsage.OnSpeechSessionStarted();
        _speech.Result += OnSpeechResult;
        _speech.Error += (_, msg) => PostStatus(msg);
        _scheduler.OcrReady += OnOcrReady;
        _scheduler.Status += (_, msg) => PostStatus(msg);
        _hotkey.Pressed += (_, _) => _ = TriggerScreenCaptureAsync();

        BrowserTargets.Add(BrowserWindowOption.Automatic);
        _browserTargetTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _browserTargetTimer.Tick += (_, _) => RefreshBrowserTargets();

        ApplyDisplayFromSettings();
    }

    // --- バインド用プロパティ ---
    public ObservableCollection<SubtitleEntry> Subtitles { get; } = new();

    public ObservableCollection<ScreenTextEntry> ScreenTexts { get; } = new();

    public ObservableCollection<BrowserWindowOption> BrowserTargets { get; } = new();

    public ScreenTranslationUsage ScreenTranslationUsage => _screenTranslationUsage;

    public BrowserWindowOption SelectedBrowserTarget
    {
        get => _selectedBrowserTarget;
        set
        {
            if (value is null || value == _selectedBrowserTarget)
            {
                return;
            }

            if (value.IsAutomatic)
            {
                _tracker.FollowAutomatically();
            }
            else if (value.Hwnd is not { } hwnd
                || (value.IsAvailable && !_tracker.PinTarget(hwnd, _settings.BrowserProcessNames))
                || (!value.IsAvailable && _tracker.PinnedHwnd != hwnd))
            {
                StatusText = "対象ウィンドウが利用できません。候補を更新しました。";
                RefreshBrowserTargets();
                return;
            }

            SetField(ref _selectedBrowserTarget, value);
            UpdateTargetBrowserText();
        }
    }

    public SubtitleEntry? InterimSubtitle
    {
        get => _interimSubtitle;
        private set => SetField(ref _interimSubtitle, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool IsAudioOn
    {
        get => _isAudioOn;
        private set
        {
            if (SetField(ref _isAudioOn, value))
            {
                OnPropertyChanged(nameof(AudioButtonText));
            }
        }
    }

    public string AudioButtonText => IsAudioOn ? "音声翻訳: ON" : "音声翻訳: OFF";

    public bool IsContinuous => _settings.ScreenMode == ScreenCaptureMode.Continuous;

    public string ScreenModeText => IsContinuous ? "画面: 常時自動" : "画面: ホットキー";

    public string AutoCaptureIntervalText => $"{Math.Max(300, _settings.AutoCaptureIntervalMs):N0} ms";

    public string OpenAIModelProfileText
        => AzureOpenAIModelProfiles.GetDisplayName(_settings.OpenAIModelProfile);

    public double FontSize
    {
        get => _fontSize;
        set
        {
            if (SetField(ref _fontSize, value))
            {
                _settings.FontSize = value;
            }
        }
    }

    public double WindowOpacity
    {
        get => _windowOpacity;
        set
        {
            if (SetField(ref _windowOpacity, value))
            {
                _settings.WindowOpacity = value;
            }
        }
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (SetField(ref _alwaysOnTop, value))
            {
                _settings.AlwaysOnTop = value;
            }
        }
    }

    public bool ShowOriginal
    {
        get => _showOriginal;
        set
        {
            if (SetField(ref _showOriginal, value))
            {
                _settings.ShowOriginal = value;
            }
        }
    }

    public string TargetBrowserText
    {
        get => _targetBrowserText;
        private set => SetField(ref _targetBrowserText, value);
    }

    /// <summary>UI 確認用のサンプルデータを投入する（--uitest 専用）。</summary>
    public void AddDemoData()
    {
        Subtitles.Add(new SubtitleEntry(
            "This is a long English sentence that should wrap onto multiple lines within the narrow application window instead of being cut off at the edge.",
            "これはアプリの狭いウィンドウ内で複数行に折り返されるべき長い英語の文章の訳です。端で切れずに枠に応じて改行されることを確認します。"));
        ScreenTexts.Add(new ScreenTextEntry("chrome",
            "Another very long original English line used to verify that text wrapping works correctly inside the screen text panel of the window.",
            "ウィンドウの画面テキストパネル内でテキストの折り返しが正しく機能することを確認するための、もう一つの非常に長い日本語の訳文サンプルです。"));
        InterimSubtitle = new SubtitleEntry(
            "an interim english subtitle line that is also fairly long to test wrapping",
            "折り返し動作を確認するための、やや長い確定前（インテリム）の日本語字幕の行です。");
        TargetBrowserText = "対象: chrome";
    }

    /// <summary>ウィンドウ表示後に呼ぶ。ホットキー登録・画面モード適用。</summary>
    public void Initialize()
    {
        if (!_hotkey.Register(_settings))
        {
            PostStatus("ホットキーの登録に失敗しました（他アプリと競合の可能性）");
        }

        if (!_ocr.IsAvailable)
        {
            PostStatus(OcrMissingMessage);
        }

        RefreshBrowserTargets();
        _browserTargetTimer.Start();
        ApplyScreenMode();
    }

    // --- 音声 ---
    public async Task ToggleAudioAsync()
    {
        if (IsAudioOn)
        {
            await StopAudioAsync();
        }
        else
        {
            await StartAudioAsync();
        }
    }

    private async Task StartAudioAsync()
    {
        if (string.IsNullOrWhiteSpace(_settings.SpeechKey))
        {
            StatusText = "Azure Speech のキーが未設定です。設定を開いてください。";
            return;
        }

        try
        {
            await _speech.StartAsync(_settings);
            _loopback.Start();
            IsAudioOn = true;
            StatusText = "音声翻訳を開始しました";
        }
        catch (Exception ex)
        {
            StatusText = $"音声開始に失敗: {ex.Message}";
            _loopback.Stop();
            await _speech.StopAsync();
        }
    }

    private async Task StopAudioAsync()
    {
        _loopback.Stop();
        await _speech.StopAsync();
        InterimSubtitle = null;
        IsAudioOn = false;
        StatusText = "音声翻訳を停止しました";
    }

    private void OnSpeechResult(object? sender, TranslationUpdate update)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (update.IsFinal)
            {
                AddSubtitle(new SubtitleEntry(update.Original, update.Translation));
                InterimSubtitle = null;
            }
            else
            {
                InterimSubtitle = new SubtitleEntry(update.Original, update.Translation);
            }
        });
    }

    private void AddSubtitle(SubtitleEntry entry)
    {
        Subtitles.Add(entry);
        while (Subtitles.Count > MaxSubtitles)
        {
            Subtitles.RemoveAt(0);
        }
    }

    // --- 画面 ---
    public void SetScreenMode(ScreenCaptureMode mode)
    {
        _settings.ScreenMode = mode;
        ApplyScreenMode();
        OnPropertyChanged(nameof(IsContinuous));
        OnPropertyChanged(nameof(ScreenModeText));
    }

    private void ApplyScreenMode()
    {
        if (_settings.ScreenMode == ScreenCaptureMode.Continuous)
        {
            _scheduler.StartContinuous();
            StatusText = "画面: 常時自動モード";
        }
        else
        {
            _scheduler.Stop();
            StatusText = "画面: ホットキーモード";
        }
    }

    public async Task TriggerScreenCaptureAsync()
    {
        if (!_ocr.IsAvailable)
        {
            StatusText = OcrMissingMessage;
            return;
        }

        try
        {
            await _scheduler.TriggerOnceAsync();
        }
        catch (Exception ex)
        {
            PostStatus($"画面翻訳エラー: {ex.Message}");
        }
    }

    private void OnOcrReady(object? sender, ScreenOcrResult result)
    {
        _dispatcher.BeginInvoke(() => _ = TranslateScreenAsync(result));
    }

    private async Task TranslateScreenAsync(ScreenOcrResult result)
    {
        var entry = new ScreenTextEntry(result.BrowserName, result.Text, "翻訳中…");
        ScreenTexts.Add(entry);
        while (ScreenTexts.Count > MaxScreenTexts)
        {
            ScreenTexts.RemoveAt(0);
        }

        try
        {
            ITextTranslator translator = GetTranslator();
            var builder = new StringBuilder();
            bool first = true;

            await foreach (string delta in translator.TranslateStreamAsync(result.Text))
            {
                if (first)
                {
                    entry.Translation = string.Empty;
                    first = false;
                }

                builder.Append(delta);
                entry.Translation = builder.ToString();
            }

            if (entry.Translation.Length == 0)
            {
                entry.Translation = "(翻訳結果がありません)";
            }
        }
        catch (Exception ex)
        {
            entry.Translation = $"(翻訳に失敗しました: {ex.Message})";
        }
    }

    private ITextTranslator GetTranslator()
    {
        _translator ??= CreateTranslator(_settings, _screenTranslationUsage);
        return _translator;
    }

    private static ITextTranslator CreateTranslator(AppSettings s, ITranslationRequestObserver requestObserver)
    {
        if (s.ScreenEngine == ScreenTranslationEngine.OpenAI &&
            !string.IsNullOrWhiteSpace(s.OpenAIEndpoint) &&
            !string.IsNullOrWhiteSpace(s.OpenAIKey) &&
            !string.IsNullOrWhiteSpace(s.OpenAIDeployment))
        {
            return new AzureOpenAITranslatorService(
                s.OpenAIEndpoint,
                s.OpenAIKey,
                s.OpenAIDeployment,
                s.OpenAIModelProfile,
                requestObserver,
                requestObserver as IModelUsageObserver);
        }

        if (s.ScreenEngine == ScreenTranslationEngine.OpenAI)
        {
            throw new InvalidOperationException(
                "Azure OpenAI 翻訳を選択していますが、エンドポイント・キー・デプロイ名が設定されていません。設定を確認してください。");
        }

        return new AzureTranslatorService(
            s.TranslatorKey, s.TranslatorRegion, s.SourceLanguage, s.TargetLanguage, requestObserver);
    }

    // --- AI回答 ---
    public async Task AnswerNowAsync()
    {
        if (!IsOpenAIConfigured())
        {
            StatusText = "AI回答には設定で Azure OpenAI（エンドポイント・キー・デプロイ名）の登録が必要です。";
            return;
        }

        if (!_ocr.IsAvailable)
        {
            StatusText = OcrMissingMessage;
            return;
        }

        StatusText = "問題テキストを取得中…";
        string question = await CaptureQuestionTextAsync();
        if (string.IsNullOrWhiteSpace(question))
        {
            StatusText = "画面から英語の問題テキストを取得できませんでした。";
            return;
        }

        string audioContext = GetRecentAudioContext();

        var entry = new ScreenTextEntry("AI回答", question, "🤖 AI回答\n生成中…");
        ScreenTexts.Add(entry);
        while (ScreenTexts.Count > MaxScreenTexts)
        {
            ScreenTexts.RemoveAt(0);
        }

        StatusText = "AI回答を生成中…";
        try
        {
            QuestionAnswerService service = GetAnswerService();
            var builder = new StringBuilder();
            await foreach (string delta in service.AnswerStreamAsync(question, audioContext))
            {
                builder.Append(delta);
                entry.Translation = "🤖 AI回答\n" + builder.ToString();
            }

            if (builder.Length == 0)
            {
                entry.Translation = "🤖 AI回答\n(回答を生成できませんでした)";
            }

            StatusText = "AI回答を生成しました";
        }
        catch (Exception ex)
        {
            entry.Translation = $"🤖 AI回答\n(生成に失敗しました: {ex.Message})";
            StatusText = "AI回答の生成に失敗しました";
        }
    }

    /// <summary>選択中のブラウザをキャプチャし、英語の問題テキストを抽出して返す（重い処理はプールスレッドで）。</summary>
    private async Task<string> CaptureQuestionTextAsync()
    {
        return await Task.Run(async () =>
        {
            CaptureTarget? target = _tracker.GetCurrentTarget(_settings.BrowserProcessNames);
            if (target is null)
            {
                return string.Empty;
            }

            using System.Drawing.Bitmap? bitmap = _screenCapture.Capture(target);
            if (bitmap is null)
            {
                return string.Empty;
            }

            string raw = await _ocr.RecognizeAsync(bitmap).ConfigureAwait(false);
            return EnglishTextExtractor.Extract(raw).Trim();
        });
    }

    /// <summary>直近の音声字幕（英語原文）をまとめて文脈として返す。リスニング問題向け。</summary>
    private string GetRecentAudioContext()
    {
        var parts = Subtitles
            .Select(s => s.Original)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts.TakeLast(20));
    }

    private bool IsOpenAIConfigured()
        => !string.IsNullOrWhiteSpace(_settings.OpenAIEndpoint)
        && !string.IsNullOrWhiteSpace(_settings.OpenAIKey)
        && !string.IsNullOrWhiteSpace(_settings.OpenAIDeployment);

    private QuestionAnswerService GetAnswerService()
    {
        _answerService ??= new QuestionAnswerService(
            _settings.OpenAIEndpoint,
            _settings.OpenAIKey,
            _settings.OpenAIDeployment,
            _settings.OpenAIModelProfile,
            _screenTranslationUsage);
        return _answerService;
    }

    // --- 設定・表示 ---
    public void OpenSettings(Window owner)
    {
        var dialog = new SettingsWindow(_settings, _store) { Owner = owner };
        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            _settings = dialog.Result;
            _translator = null; // エンジン/キー変更を反映
            _answerService = null;
            ApplyDisplayFromSettings();
            _hotkey.Register(_settings);
            ApplyScreenMode();
            OnPropertyChanged(nameof(IsContinuous));
            OnPropertyChanged(nameof(ScreenModeText));
            OnPropertyChanged(nameof(AutoCaptureIntervalText));
            OnPropertyChanged(nameof(OpenAIModelProfileText));
            RefreshBrowserTargets();
            StatusText = "設定を保存しました";
        }
    }

    public void OpenUsage(Window owner)
    {
        if (_usageWindow is { IsVisible: true } existingWindow)
        {
            existingWindow.Activate();
            return;
        }

        _usageWindow = new UsageWindow(this) { Owner = owner };
        _usageWindow.Closed += (_, _) => _usageWindow = null;
        _usageWindow.Show();
    }

    private void RefreshBrowserTargets()
    {
        BrowserWindowCandidate[] candidates = _tracker.GetCandidates(_settings.BrowserProcessNames).ToArray();
        var options = candidates.Select(candidate => BrowserWindowOption.From(candidate)).ToList();
        IntPtr? pinnedHwnd = _tracker.PinnedHwnd;
        BrowserWindowOption selected = BrowserWindowOption.Automatic;

        if (pinnedHwnd is { } hwnd)
        {
            selected = options.FirstOrDefault(option => option.Hwnd == hwnd)
                ?? new BrowserWindowOption(
                    hwnd,
                    _selectedBrowserTarget.ProcessName,
                    _selectedBrowserTarget.WindowTitle,
                    IsAutomatic: false,
                    IsAvailable: false);
            if (!options.Any(option => option.Hwnd == hwnd))
            {
                options.Insert(0, selected);
            }
        }

        options.Insert(0, BrowserWindowOption.Automatic);
        bool targetsChanged = !BrowserTargets.SequenceEqual(options);
        if (targetsChanged)
        {
            BrowserTargets.Clear();
            foreach (BrowserWindowOption option in options)
            {
                BrowserTargets.Add(option);
            }
        }

        if (targetsChanged || _selectedBrowserTarget != selected)
        {
            _selectedBrowserTarget = selected;
            OnPropertyChanged(nameof(SelectedBrowserTarget));
        }

        UpdateTargetBrowserText();
    }

    private void UpdateTargetBrowserText()
    {
        BrowserWindowCandidate? target = _tracker.GetCurrentCandidate(_settings.BrowserProcessNames);
        if (_tracker.PinnedHwnd is not null)
        {
            TargetBrowserText = target is null
                ? $"固定対象: {_selectedBrowserTarget.ProcessName} — {_selectedBrowserTarget.WindowTitle}（利用不可）"
                : $"固定対象: {BrowserWindowOption.FormatTarget(target)}";
            return;
        }

        TargetBrowserText = target is null
            ? "自動追従: 対象ブラウザ未検出"
            : $"自動追従: {BrowserWindowOption.FormatTarget(target)}";
    }

    public void AdjustFontSize(double delta)
    {
        FontSize = Math.Clamp(FontSize + delta, 10, 48);
    }

    /// <summary>字幕履歴・画面テキスト履歴・インテリム行をすべてクリアする。</summary>
    public void ClearHistory()
    {
        Subtitles.Clear();
        ScreenTexts.Clear();
        InterimSubtitle = null;
        StatusText = "履歴をクリアしました";
    }

    private void ApplyDisplayFromSettings()
    {
        FontSize = _settings.FontSize;
        WindowOpacity = _settings.WindowOpacity;
        AlwaysOnTop = _settings.AlwaysOnTop;
        ShowOriginal = _settings.ShowOriginal;
    }

    public void PersistDisplaySettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch
        {
            // best-effort（終了時の保存失敗は致命的でない）
        }
    }

    private void PostStatus(string message) => _dispatcher.BeginInvoke(() => StatusText = message);

    public void Dispose()
    {
        _browserTargetTimer.Stop();
        _hotkey.Dispose();
        _scheduler.Dispose();
        _loopback.Dispose();
        _ = _speech.DisposeAsync();
        PersistDisplaySettings();
    }
}

public sealed record BrowserWindowOption(
    IntPtr? Hwnd,
    string ProcessName,
    string WindowTitle,
    bool IsAutomatic,
    bool IsAvailable)
{
    public string DisplayName => IsAutomatic
        ? "自動追従（前面 / 前回のブラウザ）"
        : $"{ProcessName} — {(string.IsNullOrWhiteSpace(WindowTitle) ? "タイトルなし" : WindowTitle)}"
            + (IsAvailable ? $" [{Hwnd!.Value.ToInt64():X}]" : "（利用不可）");

    public static BrowserWindowOption Automatic { get; } = new(null, string.Empty, string.Empty, true, true);

    public static BrowserWindowOption From(BrowserWindowCandidate candidate)
        => new(candidate.Hwnd, candidate.ProcessName, candidate.WindowTitle, IsAutomatic: false, IsAvailable: true);

    public static string FormatTarget(BrowserWindowCandidate candidate)
        => $"{candidate.ProcessName} — {(string.IsNullOrWhiteSpace(candidate.WindowTitle) ? "タイトルなし" : candidate.WindowTitle)}";
}
