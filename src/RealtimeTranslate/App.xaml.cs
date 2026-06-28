using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using RealtimeTranslate.Config;
using RealtimeTranslate.Screen;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate;

/// <summary>アプリのエントリポイント。通常起動のほか、設定投入(--seed)・疎通確認(--selftest)モードを持つ。</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
        {
            SeedSettingsFromEnvironment();
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            // UI スレッドの同期コンテキスト捕捉によるデッドロックを避けるためプールスレッドで実行
            Task.Run(SelfTestAsync).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--selftest-screen", StringComparer.OrdinalIgnoreCase))
        {
            Task.Run(ScreenSelfTestAsync).GetAwaiter().GetResult();
            Shutdown(0);
            return;
        }

        if (e.Args.Contains("--uitest", StringComparer.OrdinalIgnoreCase))
        {
            RunUiTest();
            return;
        }

        new MainWindow().Show();
    }

    /// <summary>サンプル表示でウィンドウを描画し、PNG に保存して終了する（折り返し等の目視確認用）。</summary>
    private void RunUiTest()
    {
        var window = new MainWindow { Width = 520, Height = 720 };
        window.Show();
        ((ViewModels.MainViewModel)window.DataContext).AddDemoData();

        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
        {
            try
            {
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)window.ActualWidth, (int)window.ActualHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                string path = Path.Combine(Path.GetTempPath(), "rt_uitest.png");
                using (FileStream fs = File.Create(path))
                {
                    encoder.Save(fs);
                }
            }
            catch
            {
                // ignore
            }

            Shutdown(0);
        });
    }

    /// <summary>
    /// 保存済み設定を使って Translator/Speech の疎通を確認し、結果ファイルに書き出す（キーは出力しない）。
    /// </summary>
    private static async Task SelfTestAsync()
    {
        var store = new SettingsStore();
        AppSettings s = store.Load();
        string dir = Path.GetDirectoryName(store.FilePath) ?? Path.GetTempPath();
        var lines = new System.Collections.Generic.List<string>();

        // Translator: 実翻訳（画面テキスト経路と同じ AzureTranslatorService）
        try
        {
            var translator = new AzureTranslatorService(s.TranslatorKey, s.TranslatorRegion, "en", "ja");
            string translated = await translator.TranslateAsync("Hello, how are you?").ConfigureAwait(false);
            lines.Add($"Translator: OK -> \"{translated}\"");
        }
        catch (Exception ex)
        {
            lines.Add($"Translator: FAILED -> {ex.Message}");
        }

        // Speech: issueToken でキー有効性を確認（音声入力なしで検証可能）
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", s.SpeechKey);
            var url = $"https://{s.SpeechRegion}.api.cognitive.microsoft.com/sts/v1.0/issueToken";
            using HttpResponseMessage resp = await http.PostAsync(url, null).ConfigureAwait(false);
            string token = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            lines.Add(resp.IsSuccessStatusCode
                ? $"Speech: OK (token length={token.Length})"
                : $"Speech: FAILED -> HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            lines.Add($"Speech: FAILED -> {ex.Message}");
        }

        File.WriteAllText(Path.Combine(dir, "selftest_result.txt"), string.Join(Environment.NewLine, lines));
    }

    /// <summary>
    /// 環境変数（RT_*）から API キー等を読み取り、DPAPI 暗号化して保存する。
    /// 既存設定は保持し、与えられた項目だけ上書きする。GUI なしでの初期設定に使う。
    /// </summary>
    private static void SeedSettingsFromEnvironment()
    {
        var store = new SettingsStore();
        AppSettings settings = store.Load();

        ApplyEnv("RT_SPEECH_KEY", v => settings.SpeechKey = v);
        ApplyEnv("RT_SPEECH_REGION", v => settings.SpeechRegion = v);
        ApplyEnv("RT_TRANSLATOR_KEY", v => settings.TranslatorKey = v);
        ApplyEnv("RT_TRANSLATOR_REGION", v => settings.TranslatorRegion = v);
        ApplyEnv("RT_OPENAI_ENDPOINT", v => settings.OpenAIEndpoint = v);
        ApplyEnv("RT_OPENAI_KEY", v => settings.OpenAIKey = v);
        ApplyEnv("RT_OPENAI_DEPLOYMENT", v => settings.OpenAIDeployment = v);

        store.Save(settings);

        // 機微情報を含まない確認用の結果ファイルを書き出す（キー本体は出力しない）
        AppSettings verify = store.Load();
        string dir = Path.GetDirectoryName(store.FilePath) ?? Path.GetTempPath();
        string result =
            $"SpeechKey={State(verify.SpeechKey)} SpeechRegion={verify.SpeechRegion} " +
            $"TranslatorKey={State(verify.TranslatorKey)} TranslatorRegion={verify.TranslatorRegion} " +
            $"OpenAIKey={State(verify.OpenAIKey)} ScreenEngine={verify.ScreenEngine}";
        File.WriteAllText(Path.Combine(dir, "seed_result.txt"), result);

        static void ApplyEnv(string name, Action<string> set)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                set(value);
            }
        }

        static string State(string? value) => string.IsNullOrEmpty(value) ? "empty" : "set";
    }

    /// <summary>
    /// 画面パイプラインを境界ごとに診断する。前面ブラウザの検出→キャプチャ(黒画像か)→OCR→英語抽出を
    /// 順に記録し、どこで失敗しているかを screen_selftest_result.txt に書き出す。
    /// 起動後6秒以内に翻訳したいブラウザを前面にすること。
    /// </summary>
    private static async Task ScreenSelfTestAsync()
    {
        var store = new SettingsStore();
        AppSettings s = store.Load();
        string dir = Path.GetDirectoryName(store.FilePath) ?? Path.GetTempPath();
        var lines = new List<string>
        {
            "=== 画面翻訳 診断 ===",
            "6秒以内に、翻訳したいブラウザのウィンドウをクリックして前面にしてください...",
        };

        await Task.Delay(6000).ConfigureAwait(false);

        // [1] 前面ウィンドウ
        IntPtr fg = GetForegroundWindow();
        GetWindowThreadProcessId(fg, out uint pid);
        string fgName = "?";
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            fgName = p.ProcessName;
        }
        catch
        {
            // ignore
        }

        lines.Add($"[1] 前面ウィンドウ process='{fgName}'（対象一覧: {string.Join(", ", s.BrowserProcessNames)}）");
        lines.Add($"[OCR] 利用可能言語: {string.Join(", ", WindowsMediaOcrService.AvailableLanguages())}");

        // [2] ブラウザ検出
        var tracker = new BrowserWindowTracker();
        CaptureTarget? target = tracker.GetCurrentTarget(s.BrowserProcessNames);
        if (target is null)
        {
            lines.Add("[2] ブラウザ検出: 失敗（前面がブラウザでない、または対象一覧に無い）");
            WriteScreenResult(dir, lines);
            return;
        }

        bool contentDetected = target.ContentBounds != target.Bounds;
        lines.Add($"[2] ブラウザ検出: OK process='{target.ProcessName}'");
        lines.Add($"    ウィンドウ矩形 = {RectStr(target.Bounds)}");
        lines.Add($"    本文(UIA)矩形 = {RectStr(target.ContentBounds)} （UIAで本文検出={contentDetected}）");

        // [3] キャプチャ（全体＋本文領域を保存）
        var capture = new ScreenCaptureService();
        using System.Drawing.Bitmap? full = capture.CaptureRaw(target);
        using System.Drawing.Bitmap? content = capture.Capture(target);
        if (full is null || content is null)
        {
            lines.Add("[3] キャプチャ: 失敗(null)");
            WriteScreenResult(dir, lines);
            return;
        }

        SavePng(full, Path.Combine(dir, "rt_screen_full.png"));
        SavePng(content, Path.Combine(dir, "rt_screen_content.png"));
        lines.Add($"[3] キャプチャ: 全体 {full.Width}x{full.Height}（blank={ScreenCaptureService.IsBlank(full)}） / 本文 {content.Width}x{content.Height}");
        lines.Add("    保存: rt_screen_full.png（ウィンドウ全体）, rt_screen_content.png（OCR対象の本文領域）");

        // [4] OCR（本文領域に対して）
        var ocr = new WindowsMediaOcrService(s.SourceLanguage);
        lines.Add($"[4] OCRエンジン利用可能={ocr.IsAvailable}");
        string raw = await ocr.RecognizeAsync(content).ConfigureAwait(false);
        lines.Add($"[4] OCR生テキスト（{raw.Length}文字）:");
        lines.Add(Truncate(raw, 1000));

        // [5] 英語抽出
        string english = EnglishTextExtractor.Extract(raw);
        lines.Add($"[5] 英語抽出後（{english.Length}文字）:");
        lines.Add(Truncate(english, 1000));

        WriteScreenResult(dir, lines);
    }

    private static void WriteScreenResult(string dir, List<string> lines)
        => File.WriteAllText(Path.Combine(dir, "screen_selftest_result.txt"), string.Join(Environment.NewLine, lines));

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + " …(切り詰め)";

    private static string RectStr(System.Drawing.Rectangle r)
        => $"({r.X},{r.Y}) {r.Width}x{r.Height}";

    private static void SavePng(System.Drawing.Bitmap bitmap, string path)
    {
        try
        {
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch
        {
            // ignore
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
