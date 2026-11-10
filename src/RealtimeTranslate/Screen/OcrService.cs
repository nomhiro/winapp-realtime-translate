using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RealtimeTranslate.Screen;

/// <summary>OCR の抽象。将来 NPU 版（Windows App SDK の TextRecognizer）に差し替え可能にする拡張点。</summary>
public interface IOcrService
{
    bool IsAvailable { get; }

    Task<string> RecognizeAsync(Bitmap bitmap);
}

/// <summary>
/// Windows 標準 <see cref="OcrEngine"/>（ローカル・無料・全 Windows 10/11 で動作）による OCR。
/// 認識は数十〜数百 ms でパイプラインのボトルネックにはならない。
/// </summary>
public sealed class WindowsMediaOcrService : IOcrService
{
    private readonly OcrEngine? _engine;

    public WindowsMediaOcrService(string languageTag = "en")
    {
        try
        {
            // インストール済み OCR 言語から、要求言語（既定 en）に一致するエンジンを選ぶ。
            // 一致が無ければ engine は null（IsAvailable=false）。日本語エンジンで英語を誤読しないよう
            // 別言語へはフォールバックしない。
            Language? match = null;
            foreach (Language lang in OcrEngine.AvailableRecognizerLanguages)
            {
                if (lang.LanguageTag.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase))
                {
                    match = lang;
                    break;
                }
            }

            _engine = match is not null ? OcrEngine.TryCreateFromLanguage(match) : null;
        }
        catch
        {
            _engine = null;
        }
    }

    public bool IsAvailable => _engine is not null;

    /// <summary>この PC で OCR 可能な言語タグ一覧（診断用）。例: en-US, ja-JP。</summary>
    public static System.Collections.Generic.IReadOnlyList<string> AvailableLanguages()
    {
        try
        {
            return System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Select(OcrEngine.AvailableRecognizerLanguages, l => l.LanguageTag));
        }
        catch
        {
            return System.Array.Empty<string>();
        }
    }

    public async Task<string> RecognizeAsync(Bitmap bitmap)
    {
        if (_engine is null)
        {
            return string.Empty;
        }

        using SoftwareBitmap softwareBitmap = ToSoftwareBitmap(bitmap, (int)OcrEngine.MaxImageDimension);
        OcrResult result = await _engine.RecognizeAsync(softwareBitmap);
        return string.Join("\n", result.Lines.Select(line => line.Text));
    }

    /// <summary>
    /// <see cref="Bitmap"/> を OCR 入力用 BGRA8 <see cref="SoftwareBitmap"/> へ変換。
    /// エンコード/デコードを介さず LockBits でピクセルを直接コピーする（高速）。
    /// OCR の最大寸法を超える場合は縮小する。
    /// </summary>
    private static SoftwareBitmap ToSoftwareBitmap(Bitmap source, int maxDimension)
    {
        Bitmap working = source;
        bool disposeWorking = false;

        if (maxDimension > 0 && (source.Width > maxDimension || source.Height > maxDimension))
        {
            double scale = Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height);
            int w = Math.Max(1, (int)(source.Width * scale));
            int h = Math.Max(1, (int)(source.Height * scale));
            working = new Bitmap(source, new Size(w, h));
            disposeWorking = true;
        }

        try
        {
            var rect = new Rectangle(0, 0, working.Width, working.Height);
            BitmapData data = working.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int length = data.Stride * working.Height; // 32bpp はパディングなし (Stride == Width*4)
                var bytes = new byte[length];
                Marshal.Copy(data.Scan0, bytes, 0, length);

                return SoftwareBitmap.CreateCopyFromBuffer(
                    bytes.AsBuffer(),
                    BitmapPixelFormat.Bgra8,
                    working.Width,
                    working.Height,
                    BitmapAlphaMode.Premultiplied);
            }
            finally
            {
                working.UnlockBits(data);
            }
        }
        finally
        {
            if (disposeWorking)
            {
                working.Dispose();
            }
        }
    }
}
