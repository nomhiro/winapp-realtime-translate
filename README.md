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
- ソースからビルドする場合は [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)。GitHub Releases の自己完結版は .NET ランタイムの追加インストール不要
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
| **Azure OpenAI** ＋対応モデルのデプロイ | 画面翻訳・AI回答 | 任意 |

> 画面翻訳の既定は **Azure Translator**。OCR 誤認識の文脈補正やより自然な訳が必要なときだけ、
> 設定で **Azure OpenAI** に切り替えます。既定のモデルプロファイルは従来どおり `gpt-4.1` です。
> GPT-5.x / GPT-6 の推論モデルも選択でき、画面翻訳と AI 回答の両方で Responses API を使います。
> モデルプロファイルは API とパラメーターを明示的に選ぶための設定で、任意の Azure デプロイ名から推測されません。
> 推論設定はモデル既定を使い、Responses API の応答上限は推論トークンを含め 8192 トークンです。
> 推論トークンも出力として課金されるため、推論プロファイルでは初回応答までの時間や利用料が増えることがあります。
> モデルのリージョン提供状況・クォータ・価格はデプロイにより異なります。利用するプロファイルとデプロイ先を一致させてください。
>
> 選択できる推論モデルプロファイル（[Microsoft Learn の対応表](https://learn.microsoft.com/azure/foundry/openai/how-to/reasoning)）:
> - GPT-5.x: `gpt-5.6-sol`, `gpt-5.6-terra`, `gpt-5.6-luna`, `gpt-5.5`, `gpt-5.4-nano`, `gpt-5.4-mini`, `gpt-5.4-pro`, `gpt-5.4`, `gpt-5.3-codex`, `gpt-5.2-codex`, `gpt-5.2`, `gpt-5.1-codex-max`, `gpt-5.1`, `gpt-5.1-chat`, `gpt-5.1-codex`, `gpt-5.1-codex-mini`, `gpt-5-pro`, `gpt-5-codex`, `gpt-5`, `gpt-5-mini`, `gpt-5-nano`
> - GPT-6: `gpt-6-astra`, `gpt-6-sol`, `gpt-6-luna`, `gpt-6.1-sol`, `gpt-6.1-terra`

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
  -o publish
```

## GitHub Releases から入手する（Windows x64）

`main` へのマージ後、Windows 向け自己完結版が [GitHub Releases](https://github.com/nomhiro/winapp-realtime-translate/releases) に公開されます。PR では Windows 上の Release ビルドとテストが実行され、失敗した場合は公開されません。バージョンは `v0.1.<連番>` で、ZIP 名と Release に同じバージョンおよび対象コミットが記録されます。

1. Release の `RealtimeTranslate-v0.1.x-win-x64.zip` をダウンロードし、フォルダーへ展開します。ZIP 内のファイル一式を同じフォルダーに保ってください。
2. `RealtimeTranslate.exe` を起動します。.NET ランタイムは同梱されています。未署名のため、初回起動時に Windows のセキュリティ警告が表示されることがあります。
3. 画面翻訳には英語 OCR 言語パックが必要です（上記の手順を参照）。音声・画面翻訳を使う場合は、自分の Azure 資格情報をアプリの設定に入力してください。資格情報やユーザー設定は配布物に含まれません。
4. 更新時は新しい Release の ZIP をダウンロードし、別のフォルダーに展開して起動します。設定は `%APPDATA%\RealtimeTranslate\settings.dat` に保存されるため、更新で引き継がれます。

## 初回設定

1. アプリ起動後、ツールバーの **設定** を開く。
2. **Azure Speech** のキー・リージョンを入力。
3. **Azure Translator** のキー・リージョンを入力。
4. （任意）**Azure OpenAI** のエンドポイント・キー・モデルプロファイル・デプロイ名を入力します。
   モデルプロファイルは実際にデプロイしたモデルに合わせてください。画面翻訳に使う場合は「翻訳エンジン」を OpenAI に変更。
   既定の `gpt-4.1` は既存動作と互換性があり、GPT-5.x / GPT-6 では Responses API を使用します。
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
  - 「翻訳対象」欄には対象ブラウザ名とウィンドウタイトルが表示されます。候補から特定のウィンドウを選ぶと固定され、「自動追従」を選ぶと前面のブラウザへ追従します。同一ブラウザの複数ウィンドウもタイトルで選択できます。
  - **ホットキー**: 既定 `Ctrl+Shift+T`。押すと、選択中のブラウザを1回キャプチャして翻訳。
  - **常時自動**: 一定間隔でキャプチャし、画面に変化があったときだけ翻訳（無変化時は API を呼びません）。
  - 対象の確認・選択だけでは OCR やクラウド翻訳を実行しません。固定したウィンドウが閉じられた、最小化された、または設定の候補から外れた場合は利用不可と表示し、翻訳対象にはしません。
- **今すぐ翻訳** — モードに関係なく、選択中のブラウザを今すぐ1回翻訳。
- **履歴** — 過去の項目にスクロールすると、その位置を保ちます。各パネルの **最新へ戻る** で自動追従を再開できます。
  項目を右クリックすると、原文または訳文をコピーできます。
- **A- / A+** — フォントサイズ調整。**最前面** — 常時最前面の切替。**原文** — 原文併記の切替。

## 制限・備考

- **音声取得はシステム全体**（既定の再生デバイス出力）です。ブラウザ以外の音も混ざる場合があります。
  特定アプリのみの取得（プロセス別ループバック）は将来拡張です。
- **OCR は Windows 標準 API**（ローカル・無料、認識は数十〜数百 ms）。Copilot+ PC の NPU 版 `TextRecognizer`
  への自動アップグレードは将来拡張として `IOcrService` の差し替えで対応予定です。
- 画面キャプチャは対象ウィンドウ単位（`PrintWindow`）で、アプリ自身や他ウィンドウの重なりは拾いません。
  一部の保護コンテンツ（DRM 等）は黒画像になることがあります。
- クラウド API の利用には費用が発生します。常時自動モードは差分検出で呼び出しを抑制しています。
- Azure OpenAI の推論プロファイルは応答に使われない推論トークンも費用に含まれ、処理時間が増える場合があります。
  設定のプロファイルと Azure のデプロイが一致しない場合、API エラーを画面に表示し、別モデルへは自動で切り替えません。

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
