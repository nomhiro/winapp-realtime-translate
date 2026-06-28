using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.Translation.Text;

namespace RealtimeTranslate.Translation;

/// <summary>
/// 既定エンジン。Azure Translator による英→日テキスト翻訳。低遅延・低コストで、
/// 音声側（Speech 内蔵翻訳）と同様にリアルタイム性を優先する。
/// </summary>
public sealed class AzureTranslatorService : ITextTranslator
{
    private readonly TextTranslationClient _client;
    private readonly string _from;
    private readonly string _to;

    public AzureTranslatorService(string key, string region, string sourceLanguage, string targetLanguage)
    {
        _client = new TextTranslationClient(new AzureKeyCredential(key), region);
        _from = sourceLanguage;
        _to = targetLanguage;
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        Response<IReadOnlyList<TranslatedTextItem>> response =
            await _client.TranslateAsync(_to, text, _from, cancellationToken).ConfigureAwait(false);

        TranslatedTextItem? item = response.Value.Count > 0 ? response.Value[0] : null;
        return item is not null && item.Translations.Count > 0 ? item.Translations[0].Text : string.Empty;
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Translator はストリーミング非対応。全文を1回だけ返す。
        string result = await TranslateAsync(text, cancellationToken).ConfigureAwait(false);
        if (result.Length > 0)
        {
            yield return result;
        }
    }
}
