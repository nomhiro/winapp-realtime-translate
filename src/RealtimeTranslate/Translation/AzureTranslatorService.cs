using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly ITranslationRequestObserver? _requestObserver;

    public AzureTranslatorService(
        string key,
        string region,
        string sourceLanguage,
        string targetLanguage,
        ITranslationRequestObserver? requestObserver = null)
        : this(new TextTranslationClient(new AzureKeyCredential(key), region), sourceLanguage, targetLanguage, requestObserver)
    {
    }

    internal AzureTranslatorService(
        TextTranslationClient client,
        string sourceLanguage,
        string targetLanguage,
        ITranslationRequestObserver? requestObserver = null)
    {
        _client = client;
        _from = sourceLanguage;
        _to = targetLanguage;
        _requestObserver = requestObserver;
    }

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int[] indexes = Enumerable.Range(0, lines.Length)
            .Where(i => !string.IsNullOrWhiteSpace(lines[i])).ToArray();

        _requestObserver?.OnRequestStarted(
            indexes.Sum(i => lines[i].Length),
            TranslationProvider.Translator);
        string result;
        try
        {
            // Batch semantic lines so the service cannot flatten paragraph/list boundaries.
            Response<IReadOnlyList<TranslatedTextItem>> response =
                await _client.TranslateAsync(_to, indexes.Select(i => lines[i]), _from, cancellationToken)
                    .ConfigureAwait(false);
            if (response.Value.Count != indexes.Length || response.Value.Any(item => item.Translations.Count == 0))
            {
                throw new InvalidOperationException("Translator returned an incomplete translation.");
            }

            for (int i = 0; i < indexes.Length; i++)
            {
                lines[indexes[i]] = response.Value[i].Translations[0].Text;
            }

            result = string.Join("\n", lines);
        }
        catch (OperationCanceledException)
        {
            _requestObserver?.OnRequestCompleted(TranslationRequestOutcome.Canceled);
            throw;
        }
        catch
        {
            _requestObserver?.OnRequestCompleted(TranslationRequestOutcome.Failed);
            throw;
        }

        _requestObserver?.OnRequestCompleted(TranslationRequestOutcome.Succeeded);

        return result;
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
