using RealtimeTranslate.Config;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate.Tests;

public class ScreenTranslationUsageTests
{
    [Fact]
    public void NewUsageTrackerStartsEmpty()
    {
        var usage = new ScreenTranslationUsage();

        Assert.Equal(0, usage.TotalRequests);
        Assert.Equal(0, usage.TotalInputCharacters);
        Assert.Equal(0, usage.SuccessfulRequests);
        Assert.Equal(0, usage.FailedRequests);
        Assert.Equal(0, usage.CanceledRequests);
        Assert.Equal(0, usage.InProgressRequests);
        Assert.Null(usage.LastRequestAt);
        Assert.Equal("未実行", usage.LastRequestText);
    }

    [Fact]
    public void UsageTrackerCountsAttemptsCharactersAndOutcomes()
    {
        var usage = new ScreenTranslationUsage();

        usage.OnRequestStarted(12);
        DateTimeOffset? lastRequestAt = usage.LastRequestAt;
        usage.OnRequestStarted(7);
        usage.OnRequestCompleted(RealtimeTranslate.Translation.TranslationRequestOutcome.Succeeded);
        usage.OnRequestCompleted(RealtimeTranslate.Translation.TranslationRequestOutcome.Failed);
        usage.OnRequestStarted(3);
        usage.OnRequestCompleted(RealtimeTranslate.Translation.TranslationRequestOutcome.Canceled);

        Assert.Equal(3, usage.TotalRequests);
        Assert.Equal(22, usage.TotalInputCharacters);
        Assert.Equal(1, usage.SuccessfulRequests);
        Assert.Equal(1, usage.FailedRequests);
        Assert.Equal(1, usage.CanceledRequests);
        Assert.Equal(0, usage.InProgressRequests);
        Assert.True(usage.LastRequestAt >= lastRequestAt);
        Assert.NotEqual("未実行", usage.LastRequestText);
    }

    [Fact]
    public void UsageTrackerRejectsCompletionWithoutRequest()
    {
        var usage = new ScreenTranslationUsage();

        Assert.Throws<InvalidOperationException>(
            () => usage.OnRequestCompleted(RealtimeTranslate.Translation.TranslationRequestOutcome.Succeeded));
    }

    [Fact]
    public void UsageTrackerEstimatesSpeechAndTranslatorCosts()
    {
        var usage = new ScreenTranslationUsage();

        usage.OnSpeechSessionStarted();
        usage.OnSpeechAudioBytesWritten(96_000);
        usage.OnRequestStarted(1_000);
        usage.OnRequestCompleted(TranslationRequestOutcome.Succeeded);

        Assert.Equal(1, usage.SpeechSessionCount);
        Assert.Equal(3, usage.SpeechAudioSeconds);
        Assert.Equal(3m * 2.50m / 3600m, usage.EstimatedSpeechCostUsd);
        Assert.Equal(1_000, usage.TranslatorInputCharacters);
        Assert.Equal(0.01m, usage.EstimatedTranslatorCostUsd);
    }

    [Fact]
    public void UsageTrackerEstimatesKnownModelsAndDoesNotGuessUnknownPrices()
    {
        var knownModelUsage = new ScreenTranslationUsage();
        knownModelUsage.OnModelTokensUsed(AzureOpenAIModelProfile.Gpt41, 1_000_000, 1_000_000);

        Assert.Equal(10m, knownModelUsage.EstimatedModelCostUsd);
        Assert.Equal("US$ 10.0000", knownModelUsage.EstimatedModelCostText);

        var unknownModelUsage = new ScreenTranslationUsage();
        unknownModelUsage.OnModelTokensUsed((AzureOpenAIModelProfile)999, 1_000, 500);

        Assert.Null(unknownModelUsage.EstimatedModelCostUsd);
        Assert.Equal("単価未設定", unknownModelUsage.EstimatedModelCostText);
        Assert.Null(unknownModelUsage.EstimatedTotalCostUsd);
    }

    [Fact]
    public void EveryConfiguredModelProfileHasVerifiedGlobalStandardRates()
    {
        var expectedRates = new Dictionary<AzureOpenAIModelProfile, (decimal Input, decimal Output)>
        {
            [AzureOpenAIModelProfile.Gpt41] = (2m, 8m),
            [AzureOpenAIModelProfile.Gpt56Sol] = (4m, 20m),
            [AzureOpenAIModelProfile.Gpt56Terra] = (2m, 12m),
            [AzureOpenAIModelProfile.Gpt56Luna] = (0.20m, 1.20m),
            [AzureOpenAIModelProfile.Gpt55] = (5m, 30m),
            [AzureOpenAIModelProfile.Gpt54Nano] = (0.20m, 1.25m),
            [AzureOpenAIModelProfile.Gpt54Mini] = (0.75m, 4.50m),
            [AzureOpenAIModelProfile.Gpt54Pro] = (30m, 180m),
            [AzureOpenAIModelProfile.Gpt54] = (2.50m, 15m),
            [AzureOpenAIModelProfile.Gpt53Codex] = (1.75m, 14m),
            [AzureOpenAIModelProfile.Gpt52Codex] = (1.75m, 14m),
            [AzureOpenAIModelProfile.Gpt52] = (1.75m, 14m),
            [AzureOpenAIModelProfile.Gpt51CodexMax] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt51] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt51Chat] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt51Codex] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt51CodexMini] = (0.25m, 2m),
            [AzureOpenAIModelProfile.Gpt5Pro] = (15m, 120m),
            [AzureOpenAIModelProfile.Gpt5Codex] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt5] = (1.25m, 10m),
            [AzureOpenAIModelProfile.Gpt5Mini] = (0.25m, 2m),
            [AzureOpenAIModelProfile.Gpt5Nano] = (0.05m, 0.40m),
            [AzureOpenAIModelProfile.Gpt6Astra] = (10m, 50m),
            [AzureOpenAIModelProfile.Gpt6Sol] = (2m, 10m),
            [AzureOpenAIModelProfile.Gpt6Luna] = (0.10m, 0.50m),
            [AzureOpenAIModelProfile.Gpt61Sol] = (2m, 10m),
            [AzureOpenAIModelProfile.Gpt61Terra] = (1m, 5m),
        };

        Assert.Equal(AzureOpenAIModelProfiles.All.Count, expectedRates.Count);
        foreach ((AzureOpenAIModelProfile profile, (decimal inputRate, decimal outputRate)) in expectedRates)
        {
            var usage = new ScreenTranslationUsage();
            usage.OnModelTokensUsed(profile, 1_000_000, 1_000_000);

            Assert.Equal(inputRate + outputRate, usage.EstimatedModelCostUsd);
        }
    }
}
