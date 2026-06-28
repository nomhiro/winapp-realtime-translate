using System.Threading.Tasks;
using RealtimeTranslate.Screen;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate.Tests;

public class BrowserDetectionTests
{
    private static readonly string[] Names = { "chrome", "msedge", "firefox" };

    [Theory]
    [InlineData("chrome", true)]
    [InlineData("Chrome", true)]   // 大小無視
    [InlineData("MSEDGE", true)]
    [InlineData("notepad", false)]
    [InlineData("", false)]
    public void IsBrowserName_MatchesCaseInsensitively(string processName, bool expected)
    {
        Assert.Equal(expected, BrowserWindowTracker.IsBrowserName(processName, Names));
    }
}

public class TranslatorEmptyInputTests
{
    [Fact]
    public async Task AzureTranslator_EmptyOrWhitespace_ReturnsEmpty_WithoutNetwork()
    {
        var translator = new AzureTranslatorService("dummy-key", "japaneast", "en", "ja");
        Assert.Equal(string.Empty, await translator.TranslateAsync(""));
        Assert.Equal(string.Empty, await translator.TranslateAsync("   "));
    }

    [Fact]
    public async Task AzureOpenAI_EmptyInput_ReturnsEmpty_WithoutNetwork()
    {
        var translator = new AzureOpenAITranslatorService(
            "https://example.openai.azure.com", "dummy-key", "gpt-4.1-mini");
        Assert.Equal(string.Empty, await translator.TranslateAsync(""));
    }
}
