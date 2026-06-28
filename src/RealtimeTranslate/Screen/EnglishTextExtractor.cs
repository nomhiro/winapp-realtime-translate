using System;
using System.Collections.Generic;
using System.Text;

namespace RealtimeTranslate.Screen;

/// <summary>
/// OCR 結果から「英語の文章のみ」を抽出する。ブラウザの日本語 UI（メニュー・ブックマーク等）や
/// 記号・数字だけの行を除外し、英字が優勢な行だけを残す。純粋関数で単体テスト可能。
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

        var kept = new List<string>();
        foreach (string rawLine in ocrText.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length > 0 && IsEnglishLine(line))
            {
                kept.Add(line);
            }
        }

        return string.Join("\n", kept);
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
