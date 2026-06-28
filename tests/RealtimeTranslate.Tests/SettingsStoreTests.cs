using System;
using System.IO;
using RealtimeTranslate.Config;

namespace RealtimeTranslate.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsValues()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rt_test_{Guid.NewGuid():N}.dat");
        try
        {
            var store = new SettingsStore(path);
            var settings = new AppSettings
            {
                SpeechKey = "abc123",
                SpeechRegion = "westus",
                FontSize = 22,
                ScreenEngine = ScreenTranslationEngine.OpenAI,
                AutoCaptureIntervalMs = 999,
            };

            store.Save(settings);
            Assert.True(File.Exists(path));

            AppSettings loaded = store.Load();
            Assert.Equal("abc123", loaded.SpeechKey);
            Assert.Equal("westus", loaded.SpeechRegion);
            Assert.Equal(22, loaded.FontSize);
            Assert.Equal(ScreenTranslationEngine.OpenAI, loaded.ScreenEngine);
            Assert.Equal(999, loaded.AutoCaptureIntervalMs);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rt_missing_{Guid.NewGuid():N}.dat");
        var store = new SettingsStore(path);

        AppSettings loaded = store.Load();

        Assert.Equal("japaneast", loaded.SpeechRegion);
        Assert.Equal(ScreenTranslationEngine.Translator, loaded.ScreenEngine);
        Assert.Contains("chrome", loaded.BrowserProcessNames);
    }
}
