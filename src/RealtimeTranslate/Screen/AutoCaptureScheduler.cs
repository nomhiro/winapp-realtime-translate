using System;
using System.Threading;
using System.Threading.Tasks;
using RealtimeTranslate.Config;

namespace RealtimeTranslate.Screen;

/// <summary>OCR が得られた1件の画面テキスト。</summary>
public sealed record ScreenOcrResult(string BrowserName, string Text);

/// <summary>
/// 画面テキストパイプラインのオーケストレーション。アクティブなブラウザをキャプチャし、
/// 常時自動モードでは差分検出で変化時のみ OCR を実行する。都度モードは <see cref="TriggerOnceAsync"/>。
/// 翻訳は呼び出し側（UI）で行い、訳文を逐次表示できるようにする。
/// </summary>
public sealed class AutoCaptureScheduler : IDisposable
{
    /// <summary>平均差がこの値以上なら「変化あり」とみなす（4%）。</summary>
    private const double ChangeThreshold = 0.04;

    private readonly BrowserWindowTracker _tracker;
    private readonly ScreenCaptureService _capture;
    private readonly IOcrService _ocr;
    private readonly Func<AppSettings> _settings;

    private Timer? _timer;
    private byte[] _lastSignature = Array.Empty<byte>();
    private int _busy; // 0=idle, 1=running（重複実行防止）

    public AutoCaptureScheduler(
        BrowserWindowTracker tracker,
        ScreenCaptureService capture,
        IOcrService ocr,
        Func<AppSettings> settings)
    {
        _tracker = tracker;
        _capture = capture;
        _ocr = ocr;
        _settings = settings;
    }

    /// <summary>OCR テキストが得られたとき。</summary>
    public event EventHandler<ScreenOcrResult>? OcrReady;

    /// <summary>状態・エラーの通知（UI のステータス表示用）。</summary>
    public event EventHandler<string>? Status;

    public void StartContinuous()
    {
        Stop();
        int interval = Math.Max(300, _settings().AutoCaptureIntervalMs);
        _timer = new Timer(_ => _ = TickAsync(forceOcr: false), null, 0, interval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>都度モード: 変化の有無に関わらず1回キャプチャ→OCR する。</summary>
    public Task TriggerOnceAsync() => TickAsync(forceOcr: true);

    private async Task TickAsync(bool forceOcr)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return; // 前回処理が継続中
        }

        try
        {
            AppSettings settings = _settings();
            CaptureTarget? target = _tracker.GetCurrentTarget(settings.BrowserProcessNames);
            if (target is null)
            {
                Status?.Invoke(this, "対象のブラウザが見つかりません");
                return;
            }

            using var bitmap = _capture.Capture(target);
            if (bitmap is null)
            {
                Status?.Invoke(this, "画面のキャプチャに失敗しました");
                return;
            }

            byte[] signature = ImageSignature.Compute(bitmap);
            if (!forceOcr && _lastSignature.Length > 0 &&
                ImageSignature.Difference(signature, _lastSignature) < ChangeThreshold)
            {
                return; // 変化なし → OCR/翻訳をスキップ
            }

            _lastSignature = signature;

            string raw = await _ocr.RecognizeAsync(bitmap).ConfigureAwait(false);
            string english = EnglishTextExtractor.Extract(raw); // 英語の文章のみ抽出
            if (!string.IsNullOrWhiteSpace(english))
            {
                OcrReady?.Invoke(this, new ScreenOcrResult(target.ProcessName, english.Trim()));
            }
            else if (forceOcr)
            {
                Status?.Invoke(this, string.IsNullOrWhiteSpace(raw)
                    ? "テキストを検出できませんでした"
                    : "英語の文章を検出できませんでした");
            }
        }
        catch (Exception ex)
        {
            Status?.Invoke(this, $"画面翻訳エラー: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    public void Dispose() => Stop();
}
