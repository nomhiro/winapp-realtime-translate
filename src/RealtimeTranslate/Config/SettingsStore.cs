using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RealtimeTranslate.Config;

/// <summary>
/// <see cref="AppSettings"/> の読み書きを担う。JSON にシリアライズした内容を DPAPI
/// (<see cref="ProtectedData"/>, CurrentUser スコープ) で暗号化し、ユーザー単位で保護する。
/// API キーを平文で保存しないための実装。
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;

    public SettingsStore(string? filePath = null)
    {
        if (filePath is not null)
        {
            _filePath = filePath;
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RealtimeTranslate");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "settings.dat");
        }
    }

    public string FilePath => _filePath;

    /// <summary>
    /// 設定を読み込む。ファイルが無い／復号に失敗した場合は既定値を返す
    /// （別マシンへのコピー等で DPAPI 復号できないケースを安全に処理）。
    /// </summary>
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            byte[] encrypted = File.ReadAllBytes(_filePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 復号・パース失敗時は既定値で継続（壊れた設定で起動不能にしない）
            return new AppSettings();
        }
    }

    /// <summary>設定を DPAPI 暗号化して保存する。</summary>
    public void Save(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        byte[] plain = Encoding.UTF8.GetBytes(json);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);

        // 一時ファイルへ書いてから差し替え（書き込み中断による破損を防ぐ）
        string tmp = _filePath + ".tmp";
        File.WriteAllBytes(tmp, encrypted);
        File.Copy(tmp, _filePath, overwrite: true);
        File.Delete(tmp);
    }
}
