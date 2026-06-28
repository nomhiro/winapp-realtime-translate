using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RealtimeTranslate.Screen;

/// <summary>
/// 対象ウィンドウのピクセルをキャプチャする。GPU 描画（Chromium 等）も取り込めるよう
/// PrintWindow に PW_RENDERFULLCONTENT を指定し、失敗時は画面座標からの BitBlt にフォールバックする。
/// ウィンドウ単位で取得するため、アプリ自身や他ウィンドウの重なりを拾わない。
/// </summary>
public sealed class ScreenCaptureService
{
    private const uint PW_RENDERFULLCONTENT = 0x00000002;

    /// <summary>本文領域（<see cref="CaptureTarget.ContentBounds"/>）だけをキャプチャして返す。</summary>
    public Bitmap? Capture(CaptureTarget target)
    {
        Bitmap? full = CaptureRaw(target);
        if (full is null)
        {
            return null;
        }

        Rectangle win = target.Bounds;
        Rectangle content = Rectangle.Intersect(target.ContentBounds, win);
        if (content.Width <= 0 || content.Height <= 0 || content == win)
        {
            return full; // 本文不明 or ウィンドウ全体と同一 → そのまま
        }

        try
        {
            var rel = new Rectangle(content.X - win.X, content.Y - win.Y, content.Width, content.Height);
            Bitmap cropped = full.Clone(rel, full.PixelFormat);
            full.Dispose();
            return cropped;
        }
        catch
        {
            return full;
        }
    }

    /// <summary>ウィンドウ全体をクロップなしでキャプチャ（診断用）。</summary>
    public Bitmap? CaptureRaw(CaptureTarget target)
    {
        int width = target.Bounds.Width;
        int height = target.Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            bool printed;
            using (var g = Graphics.FromImage(bitmap))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    printed = PrintWindow(target.Hwnd, hdc, PW_RENDERFULLCONTENT);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            // PrintWindow は GPU 描画（Chromium 等）で「成功扱いだが黒画像」を返すことがある。
            // 結果がほぼ無地ならスクリーンコピーにフォールバック（対象は前面想定）。
            if (!printed || IsBlank(bitmap))
            {
                using var g2 = Graphics.FromImage(bitmap);
                g2.CopyFromScreen(target.Bounds.Left, target.Bounds.Top, 0, 0, new Size(width, height));
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            return null;
        }
    }

    /// <summary>縮小グレースケール指紋の輝度レンジがごく小さい（≒無地/黒）かどうか。</summary>
    internal static bool IsBlank(Bitmap bitmap)
    {
        byte[] signature = ImageSignature.Compute(bitmap);
        byte min = 255;
        byte max = 0;
        foreach (byte v in signature)
        {
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        return (max - min) < 8;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
}
