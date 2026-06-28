using NAudio.Wave;
using RealtimeTranslate.Audio;

namespace RealtimeTranslate.Tests;

public class PcmConverterTests
{
    [Theory]
    [InlineData(0f, 0)]
    [InlineData(1f, short.MaxValue)]
    [InlineData(-1f, -short.MaxValue)]
    [InlineData(2f, short.MaxValue)]   // クランプ
    [InlineData(-2f, -short.MaxValue)] // クランプ
    public void FloatToInt16_ScalesAndClamps(float input, int expected)
    {
        Assert.Equal((short)expected, PcmConverter.FloatToInt16(input));
    }

    [Fact]
    public void Convert_DownsamplesToTarget16kMono()
    {
        // 48kHz / 32bit float / stereo の 2 秒分（無音）を投入
        var sourceFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var converter = new PcmConverter(sourceFormat);

        const int seconds = 2;
        int inputBytes = 48000 * seconds * 2 /*ch*/ * 4 /*float*/;
        byte[] input = new byte[inputBytes];

        byte[] output = converter.Convert(input, input.Length);

        // 期待: 16kHz * 2 秒 * 2 バイト ≒ 64000 バイト（リサンプラ遅延で多少前後）
        Assert.True(output.Length % 2 == 0, "16bit PCM はバイト数が偶数になる");
        Assert.InRange(output.Length, 58000, 66000);
    }

    [Fact]
    public void Convert_EmptyInput_ReturnsEmpty()
    {
        var converter = new PcmConverter(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.Empty(converter.Convert(System.Array.Empty<byte>(), 0));
    }
}
