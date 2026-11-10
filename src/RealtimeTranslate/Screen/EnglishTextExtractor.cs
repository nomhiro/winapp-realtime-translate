using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace RealtimeTranslate.Screen;

/// <summary>
/// OCR 結果から「英語の文章のみ」を抽出する。ブラウザの日本語 UI（メニュー・ブックマーク等）や
/// 記号・数字だけの行を除外し、英字が優勢な行だけを残す。OCR の途中改行を整理し、
/// 文・段落・箇条書きの区切りは維持する。純粋関数で単体テスト可能。
/// </summary>
public static class EnglishTextExtractor
{
    /// <summary>OCR 全文から英語優勢の行だけを抽出して連結する。</summary>
    public static string Extract(string ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return string.Empty;
        }

        string[] lines = ocrText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        var kept = new List<(string Text, int Index)>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length > 0 && IsEnglishLine(line))
            {
                kept.Add((line, i));
            }
        }

        if (kept.Count == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder(kept[0].Text);
        for (int i = 1; i < kept.Count; i++)
        {
            (string previous, int previousIndex) = kept[i - 1];
            (string current, int currentIndex) = kept[i];
            result.Append(GetLineSeparator(previous, current, lines, previousIndex, currentIndex));
            result.Append(current);
        }

        return RestoreMergedQuestionLayout(result.ToString());
    }

    private static string RestoreMergedQuestionLayout(string text)
    {
        text = Regex.Replace(text, @"[ \t]+(?=No\.?\s*\d{1,3}\b)", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"[ \t]+(?=\([A-D]\)\s*)", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"[ \t]+(?=Answer\s+Sheet\b)", "\n", RegexOptions.IgnoreCase);
    }

    private static string GetLineSeparator(
        string previous,
        string current,
        string[] lines,
        int previousIndex,
        int currentIndex)
    {
        for (int i = previousIndex + 1; i < currentIndex; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                return "\n\n";
            }
        }

        if (currentIndex > previousIndex + 1)
        {
            return "\n\n";
        }

        if (IsListItem(current) || EndsSentence(previous))
        {
            return "\n";
        }

        return " ";
    }

    private static bool IsListItem(string line)
        => Regex.IsMatch(line, @"^(?:[\u2022\u2023\u25E6\u2043\u2219*+-]\s+|\d+[.)]\s+|[A-Za-z][.)]\s+|\([A-Za-z0-9]+\)\s+|No\.\s*\d+\b)");

    private static bool EndsSentence(string line)
    {
        int index = line.Length - 1;
        while (index >= 0 && line[index] is '"' or '\'' or ')' or ']' or '}' or '\u2019' or '\u201D' or '\u300D' or '\u300F')
        {
            index--;
        }

        return index >= 0 && line[index] is '.' or '!' or '?' or ':' or '\u3002' or '\uFF01' or '\uFF1F';
    }

    /// <summary>
    /// 行が英語優勢か判定する。英字が3文字以上あり、かつ日本語(CJK)文字より英字が十分多い行のみ true。
    /// </summary>
    public static bool IsEnglishLine(string line)
    {
        int latin = 0;
        int cjk = 0;
        foreach (char ch in line)
        {
            if (IsLatinLetter(ch))
            {
                latin++;
            }
            else if (IsCjk(ch))
            {
                cjk++;
            }
        }

        if (latin < 3)
        {
            return false; // 英字がほとんど無い行（記号・数字・アイコン等）は除外
        }

        // 日本語が含まれる場合は、英字が CJK の2倍以上あるときのみ英語行とみなす
        return cjk == 0 || latin >= cjk * 2;
    }

    private static bool IsLatinLetter(char c)
        => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

    private static bool IsCjk(char c)
        => (c >= 0x3040 && c <= 0x30FF)   // ひらがな・カタカナ
        || (c >= 0x4E00 && c <= 0x9FFF)   // CJK 統合漢字
        || (c >= 0x3400 && c <= 0x4DBF)   // CJK 拡張A
        || (c >= 0xFF00 && c <= 0xFFEF);  // 全角形
}
