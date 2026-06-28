using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RealtimeTranslate.Audio;

/// <summary>
/// WASAPI ループバックの出力フォーマット（通常 48kHz/32bit float/stereo）を、
/// Azure Speech が要求する <b>16kHz / 16bit / mono PCM</b> に変換する。
/// デバイス非依存の純粋ロジックなので単体テスト可能。
/// </summary>
public sealed class PcmConverter
{
    public const int TargetSampleRate = 16000;
    public const int TargetChannels = 1;
    public const int TargetBitsPerSample = 16;

    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _pipeline;
    private float[] _readBuffer = new float[8192];

    public PcmConverter(WaveFormat sourceFormat)
    {
        _buffer = new BufferedWaveProvider(sourceFormat)
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(5),
        };

        ISampleProvider sp = _buffer.ToSampleProvider();
        if (sourceFormat.Channels > 1)
        {
            sp = new MonoMixSampleProvider(sp);
        }

        _pipeline = new WdlResamplingSampleProvider(sp, TargetSampleRate);
    }

    /// <summary>出力 PCM の WaveFormat（16kHz/16bit/mono）。</summary>
    public static WaveFormat TargetFormat { get; } =
        new WaveFormat(TargetSampleRate, TargetBitsPerSample, TargetChannels);

    /// <summary>
    /// キャプチャ生バイトを取り込み、変換済みの 16bit PCM バイト列を返す。
    /// リサンプラの状態は保持されるため、チャンク境界をまたいでも連続的に変換される。
    /// </summary>
    public byte[] Convert(byte[] buffer, int bytes)
    {
        if (bytes <= 0)
        {
            return Array.Empty<byte>();
        }

        _buffer.AddSamples(buffer, 0, bytes);

        using var ms = new MemoryStream();
        int read;
        while ((read = _pipeline.Read(_readBuffer, 0, _readBuffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                short s = FloatToInt16(_readBuffer[i]);
                ms.WriteByte((byte)(s & 0xFF));
                ms.WriteByte((byte)((s >> 8) & 0xFF));
            }

            // upstream が枯渇した（要求より少ない）なら今回分は出し切った
            if (read < _readBuffer.Length)
            {
                break;
            }
        }

        return ms.ToArray();
    }

    internal static short FloatToInt16(float sample)
    {
        sample = Math.Clamp(sample, -1f, 1f);
        return (short)Math.Round(sample * short.MaxValue);
    }
}

/// <summary>
/// 任意チャンネル数の入力を全チャンネル平均でモノラル化する <see cref="ISampleProvider"/>。
/// NAudio 標準の StereoToMonoSampleProvider は 2ch 限定のため、汎用版を用意する。
/// </summary>
internal sealed class MonoMixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float[] _sourceBuffer = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    public MonoMixSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int sourceSamplesRequired = count * _channels;
        if (_sourceBuffer.Length < sourceSamplesRequired)
        {
            _sourceBuffer = new float[sourceSamplesRequired];
        }

        int read = _source.Read(_sourceBuffer, 0, sourceSamplesRequired);
        int framesRead = read / _channels;
        for (int f = 0; f < framesRead; f++)
        {
            float sum = 0f;
            for (int c = 0; c < _channels; c++)
            {
                sum += _sourceBuffer[(f * _channels) + c];
            }

            buffer[offset + f] = sum / _channels;
        }

        return framesRead;
    }
}
