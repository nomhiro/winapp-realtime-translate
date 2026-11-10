using Azure;
using Azure.AI.Translation.Text;
using RealtimeTranslate.Screen;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate.Tests;

public class StructuredTranslationTests
{
    [Fact]
    public async Task Translator_PreservesQuestionAndChoiceLayout_InOneRequest()
    {
        string original = EnglishTextExtractor.Extract(
            "No. 34 What does the man offer to do? (A) Provide a discount (B) Issue a refund " +
            "Answer Sheet No. 35 Where do they work? (A) At a hotel (B) At a restaurant");
        string[] translations =
        [
            "34番 男性は何を提案しますか？", "(A) 割引を提供する", "(B) 返金する", "解答用紙",
            "35番 どこで働いていますか？", "(A) ホテル", "(B) レストラン"
        ];
        var client = new RecordingClient(translations);
        var translator = new AzureTranslatorService(client, "en", "ja");

        string result = await translator.TranslateAsync(original);

        Assert.Equal(original.Split('\n'), client.Input);
        Assert.Equal(1, client.Calls);
        Assert.Equal(string.Join("\n", translations), result);
    }

    [Fact]
    public async Task Translator_StreamAndCompletedResultKeepTheSameParagraphBreaks()
    {
        var client = new RecordingClient(["最初の文。次の文。", "次の段落。", "(A) 選択肢"]);
        var translator = new AzureTranslatorService(client, "en", "ja");
        const string input = "First sentence. Second sentence.\r\n\r\nNext paragraph.\r\n(A) Choice";
        const string expected = "最初の文。次の文。\n\n次の段落。\n(A) 選択肢";
        var chunks = new List<string>();
        await foreach (string chunk in translator.TranslateStreamAsync(input))
        {
            chunks.Add(chunk);
        }

        Assert.Equal(expected, Assert.Single(chunks));
        Assert.Equal(expected, await translator.TranslateAsync(input));
        Assert.Equal(["First sentence. Second sentence.", "Next paragraph.", "(A) Choice"], client.Input);
    }

    [Fact]
    public async Task Translator_IncompleteResponseIsAnErrorRatherThanDroppedText()
    {
        var translator = new AzureTranslatorService(new RecordingClient(["最初の文。"]), "en", "ja");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => translator.TranslateAsync("First sentence.\nSecond sentence."));
    }

    [Fact]
    public async Task Translator_ReportsOneSuccessfulAttemptAndSubmittedTextCharacters()
    {
        var observer = new RecordingRequestObserver();
        var translator = new AzureTranslatorService(
            new RecordingClient(["最初", "次"]), "en", "ja", observer);

        await translator.TranslateAsync("First\n \nSecond");

        Assert.Equal([11], observer.StartedCharacterCounts);
        Assert.Equal([RealtimeTranslate.Translation.TranslationRequestOutcome.Succeeded], observer.Outcomes);
    }

    [Fact]
    public async Task Translator_ReportsFailedAndCanceledAttempts()
    {
        var failedObserver = new RecordingRequestObserver();
        var failedTranslator = new AzureTranslatorService(
            new RecordingClient([], new InvalidOperationException("request failed")), "en", "ja", failedObserver);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failedTranslator.TranslateAsync("Failure"));

        var canceledObserver = new RecordingRequestObserver();
        var canceledTranslator = new AzureTranslatorService(
            new RecordingClient([], new OperationCanceledException()), "en", "ja", canceledObserver);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledTranslator.TranslateAsync("Cancel"));

        Assert.Equal([RealtimeTranslate.Translation.TranslationRequestOutcome.Failed], failedObserver.Outcomes);
        Assert.Equal([RealtimeTranslate.Translation.TranslationRequestOutcome.Canceled], canceledObserver.Outcomes);
    }

    [Fact]
    public async Task Translator_EmptyInputDoesNotStartAnApiAttempt()
    {
        var observer = new RecordingRequestObserver();
        var translator = new AzureTranslatorService(new RecordingClient([]), "en", "ja", observer);

        Assert.Equal(string.Empty, await translator.TranslateAsync(" \r\n "));
        Assert.Empty(observer.StartedCharacterCounts);
        Assert.Empty(observer.Outcomes);
    }

    private sealed class RecordingClient(string[] translations, Exception? exception = null) : TextTranslationClient
    {
        public string[] Input { get; private set; } = [];
        public int Calls { get; private set; }

        public override Task<Response<IReadOnlyList<TranslatedTextItem>>> TranslateAsync(
            string targetLanguage,
            IEnumerable<string> content,
            string sourceLanguage = default!,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal("ja", targetLanguage);
            Assert.Equal("en", sourceLanguage);
            Calls++;
            if (exception is not null)
            {
                throw exception;
            }

            Input = content.ToArray();
            IReadOnlyList<TranslatedTextItem> result = translations.Select(text =>
                TranslationTextModelFactory.TranslatedTextItem(translations:
                    [TranslationTextModelFactory.TranslationText(language: "ja", text: text)])).ToArray();
            return Task.FromResult(Response.FromValue(result, null!));
        }
    }

    private sealed class RecordingRequestObserver : RealtimeTranslate.Translation.ITranslationRequestObserver
    {
        public List<int> StartedCharacterCounts { get; } = [];

        public List<RealtimeTranslate.Translation.TranslationRequestOutcome> Outcomes { get; } = [];

        public void OnRequestStarted(int inputCharacters) => StartedCharacterCounts.Add(inputCharacters);

        public void OnRequestCompleted(RealtimeTranslate.Translation.TranslationRequestOutcome outcome)
            => Outcomes.Add(outcome);
    }
}
