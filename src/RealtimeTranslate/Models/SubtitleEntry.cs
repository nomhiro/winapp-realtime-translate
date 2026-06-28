using RealtimeTranslate.Mvvm;

namespace RealtimeTranslate.Models;

/// <summary>音声翻訳の1行（原文＝英語、訳文＝日本語）。インテリム更新のため可変。</summary>
public sealed class SubtitleEntry : ObservableObject
{
    private string _original = string.Empty;
    private string _translation = string.Empty;

    public SubtitleEntry(string original, string translation)
    {
        _original = original;
        _translation = translation;
    }

    public string Original
    {
        get => _original;
        set => SetField(ref _original, value);
    }

    public string Translation
    {
        get => _translation;
        set => SetField(ref _translation, value);
    }
}
