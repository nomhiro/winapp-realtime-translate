using RealtimeTranslate.Screen;

namespace RealtimeTranslate.Tests;

public class EnglishTextExtractorTests
{
    [Theory]
    [InlineData("Hello, how are you?", true)]
    [InlineData("Chapter 1: Introduction to Programming", true)]
    [InlineData("ファイル 編集 表示 履歴 ブックマーク", false)] // 日本語UI
    [InlineData("翻訳する", false)]
    [InlineData("12:34", false)]                                 // 数字・記号のみ
    [InlineData("OK", false)]                                    // 英字2文字未満相当（<3）
    [InlineData("Overview 概要", true)]                          // 英語優勢の混在は残す
    [InlineData("概要を表示 settings 設定 ファイル メニュー", false)] // 日本語優勢は除外
    public void IsEnglishLine_ClassifiesCorrectly(string line, bool expected)
    {
        Assert.Equal(expected, EnglishTextExtractor.IsEnglishLine(line));
    }

    [Fact]
    public void Extract_KeepsOnlyEnglishLines()
    {
        string ocr = string.Join("\n", new[]
        {
            "ファイル 編集 表示",          // 除外
            "The quick brown fox",         // 残す
            "翻訳 設定",                   // 除外
            "jumps over the lazy dog.",    // 残す
            "12:34 PM",                    // 除外（英字<3）… PM は2文字
        });

        string result = EnglishTextExtractor.Extract(ocr);

        Assert.Contains("The quick brown fox", result);
        Assert.Contains("jumps over the lazy dog.", result);
        Assert.DoesNotContain("ファイル", result);
        Assert.DoesNotContain("翻訳", result);
    }

    [Fact]
    public void Extract_EmptyOrJapaneseOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EnglishTextExtractor.Extract(""));
        Assert.Equal(string.Empty, EnglishTextExtractor.Extract("これは日本語のみ\nメニュー\n設定"));
    }
}
