using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.AI.OpenAI;
using RealtimeTranslate.Config;
using RealtimeTranslate.Translation;
using OpenAI.Chat;

namespace RealtimeTranslate.Llm;

/// <summary>
/// 画面の問題文（英語）と任意の音声トランスクリプトをもとに、Azure OpenAI で答えと理由を生成する。
/// 結果はストリーミングで逐次返す。
/// </summary>
public sealed class QuestionAnswerService
{
    private const string SystemPrompt =
        "あなたは試験・クイズ・練習問題の問題に答える学習アシスタントです。" +
        "与えられた『画面の問題テキスト（英語）』と、任意の『音声の内容（リスニング問題の音声トランスクリプト）』をもとに、" +
        "最も適切な答えを判断してください。日本語で簡潔に、次の形式で出力します:\n" +
        "■答え: （選択肢があればその記号と本文。記述問題なら回答内容）\n" +
        "■理由: （なぜそれが正しいと考えられるかを2〜4文で）\n" +
        "情報が不足して断定できない場合は、最も可能性が高い答えを推測として示し、その旨を理由に書いてください。";

    private readonly ChatClient? _chatClient;
    private readonly AzureOpenAIResponsesClient? _responsesClient;
    private readonly IModelUsageObserver? _usageObserver;
    private readonly AzureOpenAIModelProfile _modelProfile;

    public QuestionAnswerService(
        string endpoint,
        string key,
        string deployment,
        AzureOpenAIModelProfile modelProfile = AzureOpenAIModelProfile.Gpt41,
        IModelUsageObserver? usageObserver = null)
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

        _usageObserver = usageObserver;
        _modelProfile = modelProfile;
    }

    public async IAsyncEnumerable<string> AnswerStreamAsync(
        string questionText,
        string audioContext,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(questionText) && string.IsNullOrWhiteSpace(audioContext))
        {
            yield break;
        }

        string userContent =
            (string.IsNullOrWhiteSpace(audioContext) ? "" : $"【音声の内容】\n{audioContext}\n\n") +
            $"【画面の問題テキスト】\n{questionText}";

        if (_responsesClient is not null)
        {
            await foreach (string delta in TrackRequestAsync(
                _responsesClient.StreamTextAsync(
                    SystemPrompt,
                    userContent,
                    cancellationToken,
                    (input, output) => _usageObserver?.OnModelTokensUsed(_modelProfile, input, output)),
                cancellationToken).ConfigureAwait(false))
            {
                yield return delta;
            }

            yield break;
        }

        var messages = new ChatMessage[] { new SystemChatMessage(SystemPrompt), new UserChatMessage(userContent) };
        var options = new ChatCompletionOptions { Temperature = 0.3f };

        await foreach (StreamingChatCompletionUpdate update in TrackRequestAsync(
            ReadChatUpdatesAsync(messages, options, cancellationToken),
            cancellationToken).ConfigureAwait(false))
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

    private async IAsyncEnumerable<StreamingChatCompletionUpdate> ReadChatUpdatesAsync(
        ChatMessage[] messages,
        ChatCompletionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (StreamingChatCompletionUpdate update in
            _chatClient!.CompleteChatStreamingAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            if (update.Usage is { } usage)
            {
                _usageObserver?.OnModelTokensUsed(
                    _modelProfile,
                    usage.InputTokenCount,
                    usage.OutputTokenCount);
            }

            yield return update;
        }
    }

    private async IAsyncEnumerable<T> TrackRequestAsync<T>(
        IAsyncEnumerable<T> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _usageObserver?.OnModelRequestStarted();
        bool succeeded = false;
        try
        {
            await foreach (T item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }

            succeeded = true;
        }
        finally
        {
            TranslationRequestOutcome outcome = succeeded
                ? TranslationRequestOutcome.Succeeded
                : cancellationToken.IsCancellationRequested
                    ? TranslationRequestOutcome.Canceled
                    : TranslationRequestOutcome.Failed;
            _usageObserver?.OnModelRequestCompleted(outcome);
        }
    }
}
