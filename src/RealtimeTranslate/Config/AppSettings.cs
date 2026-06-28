using System.Collections.Generic;

namespace RealtimeTranslate.Config;

/// <summary>画面テキスト翻訳に使うエンジン。</summary>
public enum ScreenTranslationEngine
{
    /// <summary>既定。Azure Translator（低遅延・低コスト）。</summary>
    Translator,

    /// <summary>オプション。Azure OpenAI LLM（OCR 誤認識を文脈補正、高品質）。</summary>
    OpenAI,
}

/// <summary>画面テキスト翻訳の発動モード。</summary>
public enum ScreenCaptureMode
{
    /// <summary>ホットキー押下時に1回だけ翻訳。</summary>
    OnDemand,

    /// <summary>一定間隔で自動キャプチャし、変化時のみ翻訳。</summary>
    Continuous,
}

/// <summary>
/// アプリ全体の設定。<see cref="SettingsStore"/> 経由で DPAPI 暗号化 JSON として永続化される。
/// API キー等の機微情報を含むため、ファイルは必ず暗号化して保存する。
/// </summary>
public sealed class AppSettings
{
    // --- Azure Speech（音声認識＋翻訳） ---
    public string SpeechKey { get; set; } = "";
    public string SpeechRegion { get; set; } = "japaneast";

    // --- Azure Translator（画面テキスト翻訳・既定エンジン） ---
    public string TranslatorKey { get; set; } = "";
    public string TranslatorRegion { get; set; } = "japaneast";

    /// <summary>Translator のグローバルエンドポイント（通常は変更不要）。</summary>
    public string TranslatorEndpoint { get; set; } = "https://api.cognitive.microsofttranslator.com";

    // --- Azure OpenAI（画面テキスト翻訳・オプションの高品質モード） ---
    public string OpenAIEndpoint { get; set; } = "";
    public string OpenAIKey { get; set; } = "";

    /// <summary>デプロイ名。既定は gpt-4.1（高品質モード向け）。</summary>
    public string OpenAIDeployment { get; set; } = "gpt-4.1";

    // --- 言語（英→日固定だが将来拡張のため保持） ---
    public string SourceLanguage { get; set; } = "en";
    public string TargetLanguage { get; set; } = "ja";

    // --- 画面翻訳 ---
    public ScreenTranslationEngine ScreenEngine { get; set; } = ScreenTranslationEngine.Translator;
    public ScreenCaptureMode ScreenMode { get; set; } = ScreenCaptureMode.OnDemand;

    /// <summary>常時自動モードのキャプチャ間隔（ミリ秒）。</summary>
    public int AutoCaptureIntervalMs { get; set; } = 1500;

    /// <summary>対象とみなすブラウザのプロセス名（拡張子なし・小文字）。</summary>
    public List<string> BrowserProcessNames { get; set; } = new()
    {
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
    };

    // --- グローバルホットキー（既定 Ctrl+Shift+T） ---
    public bool HotkeyCtrl { get; set; } = true;
    public bool HotkeyShift { get; set; } = true;
    public bool HotkeyAlt { get; set; }
    public bool HotkeyWin { get; set; }

    /// <summary>仮想キーコード（VK_T = 0x54）。</summary>
    public uint HotkeyVirtualKey { get; set; } = 0x54;

    // --- 表示 ---
    public double FontSize { get; set; } = 18;
    public double WindowOpacity { get; set; } = 0.95;
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>原文も併記するか（false なら訳文のみ）。</summary>
    public bool ShowOriginal { get; set; } = true;
}
