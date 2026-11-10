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
    public void Extract_MergesOcrWrapsAndPreservesSentencesParagraphsAndLists()
    {
        string ocr = "The first line wraps\nonto this line.\nA second sentence.\n\n" +
            "• First list item\n• Second list item";

        string result = EnglishTextExtractor.Extract(ocr);

        Assert.Equal(
            "The first line wraps onto this line.\nA second sentence.\n\n• First list item\n• Second list item",
            result);
    }

    [Fact]
    public void Extract_RestoresQuestionAndChoiceBreaksWhenOcrReturnsOneLine()
    {
        string ocr = "*toeic. TOEIC No. 34 What does the man offer to do? " +
            "(A) Provide a discount (B) Send a tree sample (C) Extend a warranty " +
            "(D) Issue a refund Answer Sheet NO. o:oo / 1:18 No. 35 Where do the speakers work? " +
            "(A) At a hotel (B) At a department store (C) At a restaurant (D) At a call center";

        string result = EnglishTextExtractor.Extract(ocr);

        Assert.Equal(
            "*toeic. TOEIC\n" +
            "No. 34 What does the man offer to do?\n" +
            "(A) Provide a discount\n" +
            "(B) Send a tree sample\n" +
            "(C) Extend a warranty\n" +
            "(D) Issue a refund\n" +
            "Answer Sheet NO. o:oo / 1:18\n" +
            "No. 35 Where do the speakers work?\n" +
            "(A) At a hotel\n" +
            "(B) At a department store\n" +
            "(C) At a restaurant\n" +
            "(D) At a call center",
            result);
        Assert.Equal(ocr, result.Replace('\n', ' '));
        Assert.Equal(result, EnglishTextExtractor.Extract(result));
    }

    [Theory]
    [InlineData("(A) Provide a very generous\ndiscount\n(B) Extend the warranty",
        "(A) Provide a very generous discount\n(B) Extend the warranty")]
    [InlineData("First paragraph.\r\n\r\nSecond paragraph.", "First paragraph.\n\nSecond paragraph.")]
    [InlineData("He said \"Hello!\"\nThen left.", "He said \"Hello!\"\nThen left.")]
    [InlineData("Read the manual\nbefore you begin", "Read the manual before you begin")]
    [InlineData("First section\n設定\nNext section", "First section\n\nNext section")]
    [InlineData("Answer Sheet\nNo. 36 What does the man ask about?\n(A) A promotion\n(B) A position",
        "Answer Sheet\nNo. 36 What does the man ask about?\n(A) A promotion\n(B) A position")]
    public void Extract_PreservesContentAndDoesNotInventParagraphs(string input, string expected)
    {
        Assert.Equal(expected, EnglishTextExtractor.Extract(input));
    }

    [Fact]
    public void Extract_EmptyOrJapaneseOnly_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EnglishTextExtractor.Extract(""));
        Assert.Equal(string.Empty, EnglishTextExtractor.Extract("これは日本語のみ\nメニュー\n設定"));
    }
}
