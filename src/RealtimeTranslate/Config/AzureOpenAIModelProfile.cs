using System;
using System.Collections.Generic;

namespace RealtimeTranslate.Config;

/// <summary>
/// The configured model family determines the API and request parameters. It is
/// deliberately independent of the Azure deployment name, which can be arbitrary.
/// </summary>
public enum AzureOpenAIModelProfile
{
    Gpt41,
    Gpt56Sol,
    Gpt56Terra,
    Gpt56Luna,
    Gpt55,
    Gpt54Nano,
    Gpt54Mini,
    Gpt54Pro,
    Gpt54,
    Gpt53Codex,
    Gpt52Codex,
    Gpt52,
    Gpt51CodexMax,
    Gpt51,
    Gpt51Chat,
    Gpt51Codex,
    Gpt51CodexMini,
    Gpt5Pro,
    Gpt5Codex,
    Gpt5,
    Gpt5Mini,
    Gpt5Nano,
    Gpt6Astra,
    Gpt6Sol,
    Gpt6Luna,
    Gpt61Sol,
    Gpt61Terra,
}

public static class AzureOpenAIModelProfiles
{
    private static readonly IReadOnlyDictionary<AzureOpenAIModelProfile, string> ModelIds =
        new Dictionary<AzureOpenAIModelProfile, string>
        {
            [AzureOpenAIModelProfile.Gpt41] = "gpt-4.1",
            [AzureOpenAIModelProfile.Gpt56Sol] = "gpt-5.6-sol",
            [AzureOpenAIModelProfile.Gpt56Terra] = "gpt-5.6-terra",
            [AzureOpenAIModelProfile.Gpt56Luna] = "gpt-5.6-luna",
            [AzureOpenAIModelProfile.Gpt55] = "gpt-5.5",
            [AzureOpenAIModelProfile.Gpt54Nano] = "gpt-5.4-nano",
            [AzureOpenAIModelProfile.Gpt54Mini] = "gpt-5.4-mini",
            [AzureOpenAIModelProfile.Gpt54Pro] = "gpt-5.4-pro",
            [AzureOpenAIModelProfile.Gpt54] = "gpt-5.4",
            [AzureOpenAIModelProfile.Gpt53Codex] = "gpt-5.3-codex",
            [AzureOpenAIModelProfile.Gpt52Codex] = "gpt-5.2-codex",
            [AzureOpenAIModelProfile.Gpt52] = "gpt-5.2",
            [AzureOpenAIModelProfile.Gpt51CodexMax] = "gpt-5.1-codex-max",
            [AzureOpenAIModelProfile.Gpt51] = "gpt-5.1",
            [AzureOpenAIModelProfile.Gpt51Chat] = "gpt-5.1-chat",
            [AzureOpenAIModelProfile.Gpt51Codex] = "gpt-5.1-codex",
            [AzureOpenAIModelProfile.Gpt51CodexMini] = "gpt-5.1-codex-mini",
            [AzureOpenAIModelProfile.Gpt5Pro] = "gpt-5-pro",
            [AzureOpenAIModelProfile.Gpt5Codex] = "gpt-5-codex",
            [AzureOpenAIModelProfile.Gpt5] = "gpt-5",
            [AzureOpenAIModelProfile.Gpt5Mini] = "gpt-5-mini",
            [AzureOpenAIModelProfile.Gpt5Nano] = "gpt-5-nano",
            [AzureOpenAIModelProfile.Gpt6Astra] = "gpt-6-astra",
            [AzureOpenAIModelProfile.Gpt6Sol] = "gpt-6-sol",
            [AzureOpenAIModelProfile.Gpt6Luna] = "gpt-6-luna",
            [AzureOpenAIModelProfile.Gpt61Sol] = "gpt-6.1-sol",
            [AzureOpenAIModelProfile.Gpt61Terra] = "gpt-6.1-terra",
        };

    public static IReadOnlyList<AzureOpenAIModelProfile> All { get; } =
        Enum.GetValues<AzureOpenAIModelProfile>();

    public static bool IsSupported(AzureOpenAIModelProfile profile) => ModelIds.ContainsKey(profile);

    public static bool UsesResponsesApi(AzureOpenAIModelProfile profile)
        => profile != AzureOpenAIModelProfile.Gpt41 && IsSupported(profile);

    public static string GetModelId(AzureOpenAIModelProfile profile)
        => ModelIds.TryGetValue(profile, out string? modelId)
            ? modelId
            : throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unsupported Azure OpenAI model profile.");

    public static string GetDisplayName(AzureOpenAIModelProfile profile)
        => GetModelId(profile);
}
