using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace RealtimeTranslate.Translation;

/// <summary>
/// オプションの高品質エンジン。Azure OpenAI の LLM（既定 gpt-4.1）で英→日翻訳する。
/// OCR の軽微な誤認識を文脈で補正でき、より自然な訳になる。翻訳は推論不要のため非推論モデルを使い、
/// トークンをストリーミングで受け取って逐次表示する。
/// </summary>
public sealed class AzureOpenAITranslatorService : ITextTranslator
{
    private const string SystemPrompt =
        "You are a professional translator. Translate the user's text from English into natural, fluent Japanese. " +
        "The input may come from OCR and contain minor recognition errors — infer the intended words from context. " +
        "Output only the Japanese translation with no explanations, notes, or quotation marks.";

    private readonly ChatClient _chatClient;

    public AzureOpenAITranslatorService(string endpoint, string key, string deployment)
    {
        var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
        _chatClient = client.GetChatClient(deployment);
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

        await foreach (StreamingChatCompletionUpdate update in
            _chatClient.CompleteChatStreamingAsync(BuildMessages(text), Options, cancellationToken).ConfigureAwait(false))
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
