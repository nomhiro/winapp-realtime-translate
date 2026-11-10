using System.Globalization;
using RealtimeTranslate.Audio;
using RealtimeTranslate.Config;
using RealtimeTranslate.Mvvm;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate;

public sealed class ScreenTranslationUsage : ObservableObject, ITranslationRequestObserver, IModelUsageObserver
{
    private const int SpeechBytesPerSecond =
        PcmConverter.TargetSampleRate * PcmConverter.TargetChannels * PcmConverter.TargetBitsPerSample / 8;

    private readonly object _gate = new();
    private readonly Dictionary<AzureOpenAIModelProfile, (long Input, long Output)> _modelTokenUsage = [];
    private long _totalRequests;
    private long _totalInputCharacters;
    private long _translatorInputCharacters;
    private long _successfulRequests;
    private long _failedRequests;
    private long _canceledRequests;
    private long _inProgressRequests;
    private DateTimeOffset? _lastRequestAt;
    private long _speechAudioBytes;
    private long _speechSessionCount;
    private long _modelRequests;
    private long _modelSuccessfulRequests;
    private long _modelFailedRequests;
    private long _modelCanceledRequests;
    private long _modelInProgressRequests;
    private long _totalModelInputTokens;
    private long _totalModelOutputTokens;
    private DateTimeOffset? _lastModelRequestAt;
    private long _notifiedSpeechSeconds;

    public long TotalRequests
    {
        get { lock (_gate) return _totalRequests; }
    }

    public long TotalInputCharacters
    {
        get { lock (_gate) return _totalInputCharacters; }
    }

    public long TranslatorInputCharacters
    {
        get { lock (_gate) return _translatorInputCharacters; }
    }

    public long SuccessfulRequests
    {
        get { lock (_gate) return _successfulRequests; }
    }

    public long FailedRequests
    {
        get { lock (_gate) return _failedRequests; }
    }

    public long CanceledRequests
    {
        get { lock (_gate) return _canceledRequests; }
    }

    public long InProgressRequests
    {
        get { lock (_gate) return _inProgressRequests; }
    }

    public DateTimeOffset? LastRequestAt
    {
        get { lock (_gate) return _lastRequestAt; }
    }

    public long SpeechAudioSeconds
    {
        get { lock (_gate) return _speechAudioBytes / SpeechBytesPerSecond; }
    }

    public long SpeechSessionCount
    {
        get { lock (_gate) return _speechSessionCount; }
    }

    public long ModelRequests
    {
        get { lock (_gate) return _modelRequests; }
    }

    public long ModelSuccessfulRequests
    {
        get { lock (_gate) return _modelSuccessfulRequests; }
    }

    public long ModelFailedRequests
    {
        get { lock (_gate) return _modelFailedRequests; }
    }

    public long ModelCanceledRequests
    {
        get { lock (_gate) return _modelCanceledRequests; }
    }

    public long ModelInProgressRequests
    {
        get { lock (_gate) return _modelInProgressRequests; }
    }

    public long TotalModelInputTokens
    {
        get { lock (_gate) return _totalModelInputTokens; }
    }

    public long TotalModelOutputTokens
    {
        get { lock (_gate) return _totalModelOutputTokens; }
    }

    public DateTimeOffset? LastModelRequestAt
    {
        get { lock (_gate) return _lastModelRequestAt; }
    }

    public decimal? EstimatedSpeechCostUsd => SpeechAudioSeconds * 2.50m / 3600m;

    public decimal? EstimatedTranslatorCostUsd => TranslatorInputCharacters * 10m / 1_000_000m;

    public string EstimatedSpeechCostText => FormatCost(EstimatedSpeechCostUsd);

    public string EstimatedTranslatorCostText => FormatCost(EstimatedTranslatorCostUsd);

    public decimal? EstimatedModelCostUsd
    {
        get
        {
            lock (_gate)
            {
                decimal estimate = 0;
                foreach ((AzureOpenAIModelProfile profile, (long input, long output)) in _modelTokenUsage)
                {
                    if (!TryGetModelRates(profile, out decimal inputRate, out decimal outputRate))
                    {
                        return null;
                    }

                    estimate += input * inputRate / 1_000_000m + output * outputRate / 1_000_000m;
                }

                return estimate;
            }
        }
    }

    public decimal? EstimatedTotalCostUsd
    {
        get
        {
            decimal? modelCost = EstimatedModelCostUsd;
            return modelCost is null
                ? null
                : EstimatedSpeechCostUsd + EstimatedTranslatorCostUsd + modelCost.Value;
        }
    }

    public string EstimatedModelCostText => FormatCost(EstimatedModelCostUsd);

    public string EstimatedTotalCostText
        => EstimatedTotalCostUsd is decimal estimate ? $"US$ {estimate:N4}" : "一部モデルの単価が未設定";

    public string LastRequestText
    {
        get
        {
            DateTimeOffset? lastRequestAt = LastRequestAt;
            return lastRequestAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture)
                ?? "未実行";
        }
    }

    public void OnRequestStarted(int inputCharacters)
        => OnRequestStarted(inputCharacters, TranslationProvider.Translator);

    public void OnRequestStarted(int inputCharacters, TranslationProvider provider)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacters);

        lock (_gate)
        {
            _totalRequests++;
            _totalInputCharacters += inputCharacters;
            if (provider == TranslationProvider.Translator)
            {
                _translatorInputCharacters += inputCharacters;
            }
            _inProgressRequests++;
            _lastRequestAt = DateTimeOffset.Now;
        }

        OnPropertyChanged(nameof(TotalRequests));
        OnPropertyChanged(nameof(TotalInputCharacters));
        OnPropertyChanged(nameof(TranslatorInputCharacters));
        OnPropertyChanged(nameof(InProgressRequests));
        OnPropertyChanged(nameof(EstimatedTranslatorCostUsd));
        OnPropertyChanged(nameof(EstimatedTranslatorCostText));
        OnPropertyChanged(nameof(EstimatedTotalCostUsd));
        OnPropertyChanged(nameof(EstimatedTotalCostText));
        OnPropertyChanged(nameof(LastRequestAt));
        OnPropertyChanged(nameof(LastRequestText));
    }

    public void OnRequestCompleted(TranslationRequestOutcome outcome)
    {
        lock (_gate)
        {
            if (_inProgressRequests == 0)
            {
                throw new InvalidOperationException("A translation request completed without a matching start.");
            }

            switch (outcome)
            {
                case TranslationRequestOutcome.Succeeded:
                    _successfulRequests++;
                    break;
                case TranslationRequestOutcome.Failed:
                    _failedRequests++;
                    break;
                case TranslationRequestOutcome.Canceled:
                    _canceledRequests++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }

            _inProgressRequests--;
        }

        OnPropertyChanged(nameof(SuccessfulRequests));
        OnPropertyChanged(nameof(FailedRequests));
        OnPropertyChanged(nameof(CanceledRequests));
        OnPropertyChanged(nameof(InProgressRequests));
        OnPropertyChanged(nameof(EstimatedTranslatorCostUsd));
        OnPropertyChanged(nameof(EstimatedTranslatorCostText));
        OnPropertyChanged(nameof(EstimatedTotalCostUsd));
        OnPropertyChanged(nameof(EstimatedTotalCostText));
    }

    public void OnSpeechSessionStarted()
    {
        lock (_gate)
        {
            _speechSessionCount++;
        }

        OnPropertyChanged(nameof(SpeechSessionCount));
    }

    public void OnSpeechAudioBytesWritten(int byteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteCount);

        long currentSeconds;
        lock (_gate)
        {
            _speechAudioBytes += byteCount;
            currentSeconds = _speechAudioBytes / SpeechBytesPerSecond;
            if (currentSeconds <= _notifiedSpeechSeconds)
            {
                return;
            }

            _notifiedSpeechSeconds = currentSeconds;
        }

        OnPropertyChanged(nameof(SpeechAudioSeconds));
        OnPropertyChanged(nameof(EstimatedSpeechCostUsd));
        OnPropertyChanged(nameof(EstimatedSpeechCostText));
        OnPropertyChanged(nameof(EstimatedTotalCostUsd));
        OnPropertyChanged(nameof(EstimatedTotalCostText));
    }

    public void OnModelRequestStarted()
    {
        lock (_gate)
        {
            _modelRequests++;
            _modelInProgressRequests++;
            _lastModelRequestAt = DateTimeOffset.Now;
        }

        OnPropertyChanged(nameof(ModelRequests));
        OnPropertyChanged(nameof(ModelInProgressRequests));
        OnPropertyChanged(nameof(LastModelRequestAt));
    }

    public void OnModelTokensUsed(AzureOpenAIModelProfile profile, int inputTokens, int outputTokens)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        lock (_gate)
        {
            _totalModelInputTokens += inputTokens;
            _totalModelOutputTokens += outputTokens;
            (long input, long output) = _modelTokenUsage.GetValueOrDefault(profile);
            _modelTokenUsage[profile] = (input + inputTokens, output + outputTokens);
        }

        OnPropertyChanged(nameof(TotalModelInputTokens));
        OnPropertyChanged(nameof(TotalModelOutputTokens));
        OnPropertyChanged(nameof(EstimatedModelCostUsd));
        OnPropertyChanged(nameof(EstimatedModelCostText));
        OnPropertyChanged(nameof(EstimatedTotalCostUsd));
        OnPropertyChanged(nameof(EstimatedTotalCostText));
    }

    public void OnModelRequestCompleted(TranslationRequestOutcome outcome)
    {
        lock (_gate)
        {
            if (_modelInProgressRequests == 0)
            {
                throw new InvalidOperationException("A model request completed without a matching start.");
            }

            switch (outcome)
            {
                case TranslationRequestOutcome.Succeeded:
                    _modelSuccessfulRequests++;
                    break;
                case TranslationRequestOutcome.Failed:
                    _modelFailedRequests++;
                    break;
                case TranslationRequestOutcome.Canceled:
                    _modelCanceledRequests++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
            }

            _modelInProgressRequests--;
        }

        OnPropertyChanged(nameof(ModelSuccessfulRequests));
        OnPropertyChanged(nameof(ModelFailedRequests));
        OnPropertyChanged(nameof(ModelCanceledRequests));
        OnPropertyChanged(nameof(ModelInProgressRequests));
    }

    private static bool TryGetModelRates(
        AzureOpenAIModelProfile profile,
        out decimal inputRate,
        out decimal outputRate)
    {
        (inputRate, outputRate) = profile switch
        {
            AzureOpenAIModelProfile.Gpt41 => (2m, 8m),
            AzureOpenAIModelProfile.Gpt56Sol => (4m, 20m),
            AzureOpenAIModelProfile.Gpt56Terra => (2m, 12m),
            AzureOpenAIModelProfile.Gpt56Luna => (0.20m, 1.20m),
            AzureOpenAIModelProfile.Gpt55 => (5m, 30m),
            AzureOpenAIModelProfile.Gpt54Nano => (0.20m, 1.25m),
            AzureOpenAIModelProfile.Gpt54Mini => (0.75m, 4.50m),
            AzureOpenAIModelProfile.Gpt54Pro => (30m, 180m),
            AzureOpenAIModelProfile.Gpt54 => (2.50m, 15m),
            AzureOpenAIModelProfile.Gpt53Codex => (1.75m, 14m),
            AzureOpenAIModelProfile.Gpt52Codex => (1.75m, 14m),
            AzureOpenAIModelProfile.Gpt52 => (1.75m, 14m),
            AzureOpenAIModelProfile.Gpt51CodexMax => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt51 => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt51Chat => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt51Codex => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt51CodexMini => (0.25m, 2m),
            AzureOpenAIModelProfile.Gpt5Pro => (15m, 120m),
            AzureOpenAIModelProfile.Gpt5Codex => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt5 => (1.25m, 10m),
            AzureOpenAIModelProfile.Gpt5Mini => (0.25m, 2m),
            AzureOpenAIModelProfile.Gpt5Nano => (0.05m, 0.40m),
            AzureOpenAIModelProfile.Gpt6Astra => (10m, 50m),
            AzureOpenAIModelProfile.Gpt6Sol => (2m, 10m),
            AzureOpenAIModelProfile.Gpt6Luna => (0.10m, 0.50m),
            AzureOpenAIModelProfile.Gpt61Sol => (2m, 10m),
            AzureOpenAIModelProfile.Gpt61Terra => (1m, 5m),
            _ => (0m, 0m),
        };
        return AzureOpenAIModelProfiles.IsSupported(profile);
    }

    private static string FormatCost(decimal? cost)
        => cost is decimal amount ? $"US$ {amount:N4}" : "単価未設定";
}
