using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.Translation;
using RealtimeTranslate.Config;

namespace RealtimeTranslate.Audio;

/// <summary>音声認識＋翻訳の1更新。<see cref="IsFinal"/> が false なら途中経過（インテリム）。</summary>
public sealed record TranslationUpdate(string Original, string Translation, bool IsFinal);

/// <summary>
/// Azure Speech <see cref="TranslationRecognizer"/> をプッシュストリームで駆動し、
/// 連続的に英語音声を認識して日本語へ翻訳する。途中経過と確定結果をイベントで通知し、
/// ネットワーク断などの Canceled では指数バックオフで自動再接続する。
/// </summary>
public sealed class SpeechTranslationService : IAsyncDisposable
{
    public event EventHandler<TranslationUpdate>? Result;
    public event EventHandler<string>? Error;
    public event EventHandler<bool>? RunningChanged;
    public event Action<int>? AudioBytesWritten;
    public event EventHandler? SessionStarted;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PushAudioInputStream? _pushStream;
    private TranslationRecognizer? _recognizer;
    private AppSettings? _settings;
    private CancellationTokenSource? _cts;
    private string _targetLang = "ja";
    private int _reconnectAttempt;
    private volatile bool _running;

    public bool IsRunning => _running;

    public async Task StartAsync(AppSettings settings)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);

            _settings = settings;
            _targetLang = settings.TargetLanguage;
            _cts = new CancellationTokenSource();
            await StartRecognizerAsync(settings).ConfigureAwait(false);

            _running = true;
            RunningChanged?.Invoke(this, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>変換済み 16kHz/16bit/mono PCM をストリームへ供給する。</summary>
    public void Write(byte[] pcm)
    {
        try
        {
            PushAudioInputStream? stream = _pushStream;
            if (stream is null)
            {
                return;
            }

            stream.Write(pcm);
        }
        catch
        {
            // ストリームが再構築中などのタイミングは無視（次チャンクで復帰）
            return;
        }

        AudioBytesWritten?.Invoke(pcm.Length);
    }

    private async Task StartRecognizerAsync(AppSettings settings)
    {
        var config = SpeechTranslationConfig.FromSubscription(settings.SpeechKey, settings.SpeechRegion);
        config.SpeechRecognitionLanguage = settings.SourceLanguage == "en" ? "en-US" : settings.SourceLanguage;
        config.AddTargetLanguage(settings.TargetLanguage);

        var format = AudioStreamFormat.GetWaveFormatPCM(
            (uint)PcmConverter.TargetSampleRate,
            (byte)PcmConverter.TargetBitsPerSample,
            (byte)PcmConverter.TargetChannels);

        _pushStream = AudioInputStream.CreatePushStream(format);
        var audioConfig = AudioConfig.FromStreamInput(_pushStream);

        _recognizer = new TranslationRecognizer(config, audioConfig);
        _recognizer.Recognizing += OnRecognizing;
        _recognizer.Recognized += OnRecognized;
        _recognizer.Canceled += OnCanceled;

        await _recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
        SessionStarted?.Invoke(this, EventArgs.Empty);
        _reconnectAttempt = 0;
    }

    private async Task StopInternalAsync()
    {
        _running = false;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignore
        }

        await StopRecognizerAsync().ConfigureAwait(false);

        _cts?.Dispose();
        _cts = null;
        RunningChanged?.Invoke(this, false);
    }

    private async Task StopRecognizerAsync()
    {
        TranslationRecognizer? recognizer = _recognizer;
        PushAudioInputStream? stream = _pushStream;
        _recognizer = null;
        _pushStream = null;

        if (recognizer is not null)
        {
            recognizer.Recognizing -= OnRecognizing;
            recognizer.Recognized -= OnRecognized;
            recognizer.Canceled -= OnCanceled;

            try
            {
                await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }

            recognizer.Dispose();
        }

        stream?.Close();
        stream?.Dispose();
    }

    private void OnRecognizing(object? sender, TranslationRecognitionEventArgs e)
    {
        EmitResult(e, isFinal: false);
    }

    private void OnRecognized(object? sender, TranslationRecognitionEventArgs e)
    {
        if (e.Result.Reason == ResultReason.TranslatedSpeech)
        {
            EmitResult(e, isFinal: true);
        }
    }

    private void EmitResult(TranslationRecognitionEventArgs e, bool isFinal)
    {
        string original = e.Result.Text ?? string.Empty;
        e.Result.Translations.TryGetValue(_targetLang, out string? translation);

        if (string.IsNullOrWhiteSpace(original) && string.IsNullOrWhiteSpace(translation))
        {
            return;
        }

        Result?.Invoke(this, new TranslationUpdate(original, translation ?? string.Empty, isFinal));
    }

    private void OnCanceled(object? sender, TranslationRecognitionCanceledEventArgs e)
    {
        if (e.Reason == CancellationReason.Error)
        {
            Error?.Invoke(this, $"音声認識エラー: {e.ErrorCode} {e.ErrorDetails}");
            _ = TryReconnectAsync();
        }
    }

    private async Task TryReconnectAsync()
    {
        CancellationTokenSource? cts = _cts;
        if (cts is null || cts.IsCancellationRequested || !_running)
        {
            return;
        }

        int attempt = Interlocked.Increment(ref _reconnectAttempt);
        int delayMs = Math.Min(30_000, 1_000 * (int)Math.Pow(2, Math.Min(attempt - 1, 5)));

        try
        {
            await Task.Delay(delayMs, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cts.IsCancellationRequested || !_running || _settings is null)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_running)
            {
                return;
            }

            await StopRecognizerAsync().ConfigureAwait(false);
            await StartRecognizerAsync(_settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"再接続に失敗: {ex.Message}");
            _ = TryReconnectAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
