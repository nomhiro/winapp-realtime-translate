using RealtimeTranslate.Mvvm;

namespace RealtimeTranslate.Models;

/// <summary>画面 OCR テキストの1件。訳文はストリーミングで逐次更新されるため可変。</summary>
public sealed class ScreenTextEntry : ObservableObject
{
    private string _translation;

    public ScreenTextEntry(string browserName, string original, string translation)
    {
        BrowserName = browserName;
        Original = original;
        _translation = translation;
    }

    public string BrowserName { get; }

    public string Original { get; }

    public string Translation
    {
        get => _translation;
        set => SetField(ref _translation, value);
    }
}
