using RealtimeTranslate.Config;

namespace RealtimeTranslate.Translation;

public enum TranslationRequestOutcome
{
    Succeeded,
    Failed,
    Canceled,
}

public enum TranslationProvider
{
    Translator,
    OpenAI,
}

public interface ITranslationRequestObserver
{
    /// <summary>Called as the screen-translation service begins an API request; only the character count is reported.</summary>
    void OnRequestStarted(int inputCharacters);

    void OnRequestStarted(int inputCharacters, TranslationProvider provider)
        => OnRequestStarted(inputCharacters);

    /// <summary>Called when the API request completes, fails, or is canceled.</summary>
    void OnRequestCompleted(TranslationRequestOutcome outcome);
}

public interface IModelUsageObserver
{
    void OnModelRequestStarted();

    void OnModelTokensUsed(AzureOpenAIModelProfile profile, int inputTokens, int outputTokens);

    void OnModelRequestCompleted(TranslationRequestOutcome outcome);
}
