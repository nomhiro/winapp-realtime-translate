using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using RealtimeTranslate.Config;

namespace RealtimeTranslate.Views;

/// <summary>
/// 設定編集ダイアログ。渡された <see cref="AppSettings"/> のクローンを編集し、
/// 「保存」時のみ <see cref="SettingsStore"/> で永続化して結果を返す。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly AppSettings _working;

    /// <summary>保存後の設定（保存された場合のみ非 null）。</summary>
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current, SettingsStore store)
    {
        InitializeComponent();
        _store = store;

        // クローンを編集対象にする（キャンセルで元に戻せるように）
        _working = Clone(current);
        DataContext = _working;

        EngineCombo.ItemsSource = Enum.GetValues<ScreenTranslationEngine>();
        ModeCombo.ItemsSource = Enum.GetValues<ScreenCaptureMode>();
        OpenAIProfileCombo.ItemsSource = AzureOpenAIModelProfiles.All
            .Select(profile => new
            {
                Value = profile,
                Label = AzureOpenAIModelProfiles.GetDisplayName(profile),
            })
            .ToList();
        BrowserNamesBox.Text = string.Join(", ", _working.BrowserProcessNames);
        HotkeyKeyBox.Text = VirtualKeyToChar(_working.HotkeyVirtualKey);
    }

    private static AppSettings Clone(AppSettings src)
    {
        string json = JsonSerializer.Serialize(src);
        return JsonSerializer.Deserialize<AppSettings>(json)!;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        // 一覧系・ホットキーは手動で working へ反映
        _working.BrowserProcessNames = BrowserNamesBox.Text
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct()
            .ToList();

        string key = HotkeyKeyBox.Text.Trim().ToUpperInvariant();
        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z')
        {
            _working.HotkeyVirtualKey = key[0];
        }
        else if (key.Length == 1 && key[0] >= '0' && key[0] <= '9')
        {
            _working.HotkeyVirtualKey = key[0];
        }

        try
        {
            _store.Save(_working);
            Result = _working;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"設定の保存に失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string VirtualKeyToChar(uint vk)
        => vk is >= 0x30 and <= 0x5A ? ((char)vk).ToString() : "T";
}
