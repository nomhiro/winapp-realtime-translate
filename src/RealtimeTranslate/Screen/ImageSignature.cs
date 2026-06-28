using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RealtimeTranslate.Screen;

/// <summary>
/// 常時自動モードで「画面が変化したか」を判定するための簡易画像シグネチャ。
/// 小さなグレースケールに縮小して指紋を作り、平均絶対差で変化量を測る。
/// 変化がなければ OCR・翻訳をスキップして API コストと誤検出を抑える。
/// </summary>
public static class ImageSignature
{
    public const int DefaultSize = 16;

    /// <summary>ビットマップを size×size のグレースケール指紋（byte 配列）に変換する。</summary>
    public static byte[] Compute(Bitmap source, int size = DefaultSize)
    {
        using var small = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, size, size);
        }

        var signature = new byte[size * size];
        var rect = new Rectangle(0, 0, size, size);
        BitmapData data = small.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            var buffer = new byte[stride * size];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idx = (y * stride) + (x * 4);
                    byte b = buffer[idx];
                    byte gg = buffer[idx + 1];
                    byte r = buffer[idx + 2];
                    // 知覚輝度に近い重み付けでグレースケール化
                    signature[(y * size) + x] = (byte)(((r * 30) + (gg * 59) + (b * 11)) / 100);
                }
            }
        }
        finally
        {
            small.UnlockBits(data);
        }

        return signature;
    }

    /// <summary>
    /// 2つの指紋の差を 0.0（同一）～1.0（最大）で返す。長さ不一致・空は 1.0（=変化あり）。
    /// 純粋関数なので単体テスト可能。
    /// </summary>
    public static double Difference(byte[] a, byte[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length)
        {
            return 1.0;
        }

        long sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return sum / (255.0 * a.Length);
    }
}
