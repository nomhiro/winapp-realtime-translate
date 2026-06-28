using System.Drawing;
using System.Drawing.Imaging;
using RealtimeTranslate.Screen;

namespace RealtimeTranslate.Tests;

public class ImageSignatureTests
{
    [Fact]
    public void Difference_IdenticalArrays_IsZero()
    {
        byte[] a = { 10, 20, 30, 40 };
        byte[] b = { 10, 20, 30, 40 };
        Assert.Equal(0.0, ImageSignature.Difference(a, b), 5);
    }

    [Fact]
    public void Difference_MaxDifference_IsOne()
    {
        byte[] a = { 0, 0, 0, 0 };
        byte[] b = { 255, 255, 255, 255 };
        Assert.Equal(1.0, ImageSignature.Difference(a, b), 5);
    }

    [Fact]
    public void Difference_LengthMismatchOrEmpty_IsOne()
    {
        Assert.Equal(1.0, ImageSignature.Difference(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }), 5);
        Assert.Equal(1.0, ImageSignature.Difference(System.Array.Empty<byte>(), new byte[] { 1 }), 5);
    }

    [Fact]
    public void Difference_HalfShift_IsAboutHalf()
    {
        byte[] a = { 0, 0 };
        byte[] b = { 128, 128 };
        Assert.Equal(128.0 / 255.0, ImageSignature.Difference(a, b), 3);
    }

    [Fact]
    public void Compute_SolidColors_ProduceUniformSignatures_AndMaxDifference()
    {
        byte[] black = ComputeSolid(Color.Black);
        byte[] white = ComputeSolid(Color.White);

        Assert.All(black, v => Assert.True(v <= 1));
        Assert.All(white, v => Assert.True(v >= 254));
        Assert.True(ImageSignature.Difference(black, white) > 0.98);
        Assert.Equal(0.0, ImageSignature.Difference(black, black), 5);
    }

    private static byte[] ComputeSolid(Color color)
    {
        using var bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
        }

        return ImageSignature.Compute(bmp);
    }
}
