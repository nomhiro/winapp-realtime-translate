using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace RealtimeTranslate.Audio;

/// <summary>
/// 既定の再生デバイス出力（システム音声）を WASAPI ループバックで取得し、
/// 16kHz/16bit/mono PCM に変換して <see cref="PcmAvailable"/> で発行する。
/// 既定デバイスの切替を検知して自動的にキャプチャを再初期化する。
/// </summary>
public sealed class LoopbackCaptureService : IDisposable
{
    /// <summary>変換済み 16kHz/16bit/mono PCM チャンク。</summary>
    public event EventHandler<byte[]>? PcmAvailable;

    /// <summary>復旧不能なエラーの通知（UI のステータス表示用）。</summary>
    public event EventHandler<string>? Error;

    private readonly object _lock = new();
    private WasapiLoopbackCapture? _capture;
    private PcmConverter? _converter;
    private MMDeviceEnumerator? _enumerator;
    private NotificationClient? _notificationClient;
    private bool _running;

    public bool IsRunning
    {
        get { lock (_lock) { return _running; } }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_running)
            {
                return;
            }

            StartInternal();

            try
            {
                _enumerator = new MMDeviceEnumerator();
                _notificationClient = new NotificationClient(this);
                _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
            }
            catch
            {
                // デバイス通知は best-effort。失敗してもキャプチャ自体は継続。
            }

            _running = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            _running = false;

            try
            {
                if (_enumerator is not null && _notificationClient is not null)
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                }
            }
            catch
            {
                // ignore
            }

            StopInternal();
            _enumerator?.Dispose();
            _enumerator = null;
            _notificationClient = null;
        }
    }

    private void StartInternal()
    {
        _capture = new WasapiLoopbackCapture();
        _converter = new PcmConverter(_capture.WaveFormat);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
        _capture.StartRecording();
    }

    private void StopInternal()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnData;
            _capture.RecordingStopped -= OnStopped;
            try
            {
                _capture.StopRecording();
            }
            catch
            {
                // ignore
            }

            _capture.Dispose();
            _capture = null;
        }

        _converter = null;
    }

    private void RestartCapture()
    {
        lock (_lock)
        {
            if (!_running)
            {
                return;
            }

            StopInternal();
            try
            {
                StartInternal();
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, $"音声デバイスの再初期化に失敗: {ex.Message}");
            }
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        PcmConverter? converter = _converter;
        if (converter is null)
        {
            return;
        }

        try
        {
            byte[] pcm = converter.Convert(e.Buffer, e.BytesRecorded);
            if (pcm.Length > 0)
            {
                PcmAvailable?.Invoke(this, pcm);
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, $"音声変換エラー: {ex.Message}");
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Error?.Invoke(this, $"音声キャプチャ停止: {e.Exception.Message}");
        }
    }

    public void Dispose() => Stop();

    /// <summary>既定の再生デバイス変更を監視し、変更時にキャプチャを張り直す。</summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly LoopbackCaptureService _owner;

        public NotificationClient(LoopbackCaptureService owner) => _owner = owner;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia)
            {
                // コールバックをブロックしないよう別スレッドで再起動
                Task.Run(_owner.RestartCapture);
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId) { }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
