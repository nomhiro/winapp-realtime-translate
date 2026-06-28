# リアルタイム翻訳 (RealtimeTranslate)

特定のブラウザ画面に表示される **英語テキスト**（記事・スライド・試験問題など）と、
**システム音声**（ブラウザから流れる英語の発話）を、リアルタイムで日本語に翻訳し、
専用ウィンドウに原文＋訳文で表示する Windows デスクトップアプリです。

- **音声**: システム音声を WASAPI ループバックで取得 → Azure Speech で連続認識＋翻訳（インテリム字幕）
- **画面**: アクティブなブラウザウィンドウを自動検出 → Windows 標準 OCR → **英語の文章のみ抽出** → 翻訳（Azure Translator 既定）
  - 画面翻訳は「ホットキーで都度」「常時自動（変化検出）」を切替可能。**領域選択は不要**。
  - OCR 結果から日本語 UI（メニュー・ブックマーク等）や記号・数字行を除外し、英語優勢の行だけを訳します。

## 動作環境

- Windows 10 (1903 / build 18362) 以降 / Windows 11
- [.NET 9 ランタイム](https://dotnet.microsoft.com/download/dotnet/9.0)（ビルドには .NET 9 SDK）
- **英語OCR言語パック**（画面テキスト翻訳に必須）。日本語版 Windows では未導入の場合があります。

### 英語OCRのインストール（画面翻訳が動かない場合）

画面翻訳が英語を正しく読めない／関係ない文字を拾う場合、英語OCRが未インストールの可能性があります。
**管理者** PowerShell で次を実行し、アプリを再起動してください。

```powershell
# 現状確認（State が NotPresent なら未導入）
Get-WindowsCapability -Online -Name "Language.OCR*" | Select-Object Name, State
# 英語(米国)OCR を追加
Add-WindowsCapability -Online -Name "Language.OCR~~~en-US~0.0.1.0"
```

GUI の場合: 設定 → 時刻と言語 → 言語と地域 → 「English (United States)」を追加（言語機能に OCR が含まれます）。

## Azure リソースの準備

[Azure ポータル](https://portal.azure.com) で以下を作成し、キー等を控えてください。

| サービス | 用途 | 要否 |
| --- | --- | --- |
| **Speech**（Azure AI Speech） | 音声認識＋翻訳 | 必須（音声機能を使う場合） |
| **Translator**（Azure AI Translator） | 画面テキスト翻訳（既定エンジン） | 必須（画面機能を使う場合） |
| **Azure OpenAI** ＋ `gpt-4.1-mini` デプロイ | 画面翻訳の高品質モード | 任意 |

> 画面翻訳の既定は低遅延の **Azure Translator**。OCR 誤認識の文脈補正やより自然な訳が必要なときだけ、
> 設定で **Azure OpenAI（既定モデル `gpt-4.1-mini`）** に切り替えます。翻訳に推論は不要なため、推論モデル
> （gpt-5 系・o 系）は遅延が増えるだけで採用していません。

## ビルドと実行

```powershell
# 依存関係の復元とビルド
dotnet build RealtimeTranslate.sln -c Release

# 実行
dotnet run --project src/RealtimeTranslate -c Release

# テスト
dotnet test RealtimeTranslate.sln
```

配布用の自己完結 exe を作る場合:

```powershell
dotnet publish src/RealtimeTranslate -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

## 初回設定

1. アプリ起動後、ツールバーの **設定** を開く。
2. **Azure Speech** のキー・リージョンを入力。
3. **Azure Translator** のキー・リージョンを入力。
4. （任意）高品質モードを使う場合は **Azure OpenAI** のエンドポイント・キー・デプロイ名（`gpt-4.1-mini`）を入力し、
   「翻訳エンジン」を OpenAI に変更。
5. 保存。キーは **DPAPI（現在のユーザー）で暗号化**して
   `%APPDATA%\RealtimeTranslate\settings.dat` に保存されます（平文で残りません）。

### GUI を使わずに設定する（任意）

環境変数からキーを書き込む `--seed` モードがあります（CI や初期セットアップ向け。キーはソースに残りません）。

```powershell
$env:RT_SPEECH_KEY='<speech-key>'; $env:RT_SPEECH_REGION='japaneast'
$env:RT_TRANSLATOR_KEY='<translator-key>'; $env:RT_TRANSLATOR_REGION='japaneast'
# 任意: $env:RT_OPENAI_ENDPOINT / RT_OPENAI_KEY / RT_OPENAI_DEPLOYMENT
.\RealtimeTranslate.exe --seed
```

資格情報の疎通確認（Translator の実翻訳＋Speech のトークン取得）:

```powershell
.\RealtimeTranslate.exe --selftest   # 結果は %APPDATA%\RealtimeTranslate\selftest_result.txt（キーは出力されません）
```

## 使い方

- **音声翻訳: ON/OFF** — システム音声の翻訳字幕を開始/停止。再生中の英語音声がリアルタイムに字幕化されます。
  - 上段「🔊 音声翻訳」に確定字幕の履歴、最下部の枠にインテリム（確定前）が表示されます。
- **画面: ホットキー / 常時自動** — クリックで切替。
  - **ホットキー**: 既定 `Ctrl+Shift+T`。押すと、その時点のアクティブなブラウザを1回キャプチャして翻訳。
  - **常時自動**: 一定間隔でキャプチャし、画面に変化があったときだけ翻訳（無変化時は API を呼びません）。
- **今すぐ翻訳** — モードに関係なく、アクティブなブラウザを今すぐ1回翻訳。
- **A- / A+** — フォントサイズ調整。**最前面** — 常時最前面の切替。**原文** — 原文併記の切替。

## 制限・備考

- **音声取得はシステム全体**（既定の再生デバイス出力）です。ブラウザ以外の音も混ざる場合があります。
  特定アプリのみの取得（プロセス別ループバック）は将来拡張です。
- **OCR は Windows 標準 API**（ローカル・無料、認識は数十〜数百 ms）。Copilot+ PC の NPU 版 `TextRecognizer`
  への自動アップグレードは将来拡張として `IOcrService` の差し替えで対応予定です。
- 画面キャプチャは対象ウィンドウ単位（`PrintWindow`）で、アプリ自身や他ウィンドウの重なりは拾いません。
  一部の保護コンテンツ（DRM 等）は黒画像になることがあります。
- クラウド API の利用には費用が発生します。常時自動モードは差分検出で呼び出しを抑制しています。

## プロジェクト構成

```
src/RealtimeTranslate/
  Audio/      LoopbackCaptureService, PcmConverter, SpeechTranslationService
  Screen/     BrowserWindowTracker, ScreenCaptureService, OcrService, ImageSignature, AutoCaptureScheduler
  Translation/ ITextTranslator, AzureTranslatorService, AzureOpenAITranslatorService
  Hotkey/     GlobalHotkeyService
  Config/     AppSettings, SettingsStore (DPAPI)
  Models/     SubtitleEntry, ScreenTextEntry
  ViewModels/ MainViewModel
  Views/      SettingsWindow
  MainWindow.xaml
tests/RealtimeTranslate.Tests/   ユニットテスト (xUnit)
```
