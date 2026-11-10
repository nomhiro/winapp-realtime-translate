using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using RealtimeTranslate.Config;
using RealtimeTranslate.Llm;
using OpenAI.Chat;

namespace RealtimeTranslate.Translation;

/// <summary>
/// オプションの高品質エンジン。Azure OpenAI で英→日翻訳する。
/// GPT-4.1 は既存の Chat Completions API、それ以外の選択済みプロファイルは Responses API を使う。
/// </summary>
public sealed class AzureOpenAITranslatorService : ITextTranslator
{
    private const string SystemPrompt =
        "You are a professional translator. Translate the user's text from English into natural, fluent Japanese. " +
        "The input may come from OCR and contain minor recognition errors — infer the intended words from context. " +
        "Preserve meaningful sentence and paragraph breaks, and keep each bullet or numbered list item on its own line. " +
        "Do not reproduce arbitrary OCR line wraps; join text that continues the same sentence. " +
        "Output only the Japanese translation with no explanations, notes, or quotation marks.";

    private readonly ChatClient? _chatClient;
    private readonly AzureOpenAIResponsesClient? _responsesClient;

    public AzureOpenAITranslatorService(
        string endpoint,
        string key,
        string deployment,
        AzureOpenAIModelProfile modelProfile = AzureOpenAIModelProfile.Gpt41)
    {
        if (!AzureOpenAIModelProfiles.IsSupported(modelProfile))
        {
            throw new ArgumentOutOfRangeException(nameof(modelProfile), "Unsupported Azure OpenAI model profile.");
        }

        if (AzureOpenAIModelProfiles.UsesResponsesApi(modelProfile))
        {
            _responsesClient = new AzureOpenAIResponsesClient(endpoint, key, deployment);
        }
        else
        {
            var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
            _chatClient = client.GetChatClient(deployment);
        }
    }

    private static ChatCompletionOptions Options => new() { Temperature = 0.2f };

    private static IEnumerable<ChatMessage> BuildMessages(string text)
        => new ChatMessage[] { new SystemChatMessage(SystemPrompt), new UserChatMessage(text) };

    public async Task<string> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        await foreach (string delta in TranslateStreamAsync(text, cancellationToken).ConfigureAwait(false))
        {
            sb.Append(delta);
        }

        return sb.ToString();
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(
        string text,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        if (_responsesClient is not null)
        {
            await foreach (string delta in
                _responsesClient.StreamTextAsync(SystemPrompt, text, cancellationToken).ConfigureAwait(false))
            {
                yield return delta;
            }

            yield break;
        }

        await foreach (StreamingChatCompletionUpdate update in
            _chatClient!.CompleteChatStreamingAsync(BuildMessages(text), Options, cancellationToken).ConfigureAwait(false))
        {
            foreach (ChatMessageContentPart part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }
}
