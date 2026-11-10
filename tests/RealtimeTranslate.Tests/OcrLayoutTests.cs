using System.Drawing;
using System.Drawing.Imaging;
using RealtimeTranslate.Screen;

namespace RealtimeTranslate.Tests;

public class OcrLayoutTests
{
    [EnglishOcrFact]
    public async Task RecognizeAsync_PreservesPhysicalLinesBeforeEnglishExtraction()
    {
        using var bitmap = new Bitmap(1000, 300, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Arial", 28);
        graphics.Clear(Color.White);
        graphics.DrawString("First sentence ends here.", font, Brushes.Black, 20, 20);
        graphics.DrawString("Second sentence ends here.", font, Brushes.Black, 20, 100);

        string raw = await new WindowsMediaOcrService().RecognizeAsync(bitmap);

        Assert.Contains("First sentence", raw);
        Assert.Contains("Second sentence", raw);
        Assert.True(raw.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length >= 2, raw);
        Assert.Contains("\n", EnglishTextExtractor.Extract(raw));
    }

    private sealed class EnglishOcrFactAttribute : FactAttribute
    {
        public EnglishOcrFactAttribute()
        {
            if (!new WindowsMediaOcrService().IsAvailable)
            {
                Skip = "Requires the Windows English OCR language pack.";
            }
        }
    }
}
