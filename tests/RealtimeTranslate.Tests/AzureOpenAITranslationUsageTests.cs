using System.Net;
using System.Net.Http;
using System.Text;
using RealtimeTranslate.Llm;
using RealtimeTranslate.Translation;

namespace RealtimeTranslate.Tests;

public class AzureOpenAITranslationUsageTests
{
    [Fact]
    public async Task ResponsesTranslator_ReportsSuccessfulRequestAndInputCharacters()
    {
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "event: response.output_text.delta\n" +
                    "data: {\"type\":\"response.output_text.delta\",\"delta\":\"こんにちは\"}\n\n" +
                    "event: response.completed\n" +
                    "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":100,\"output_tokens\":20}}}\n\n",
                    Encoding.UTF8,
                    "text/event-stream"),
            })));
        var usage = new ScreenTranslationUsage();
        var client = new AzureOpenAIResponsesClient("https://example.test", "key", "deployment", httpClient);
        var translator = new AzureOpenAITranslatorService(
            client,
            usage,
            usage,
            RealtimeTranslate.Config.AzureOpenAIModelProfile.Gpt41);

        string translation = await translator.TranslateAsync("Hello");

        Assert.Equal("こんにちは", translation);
        Assert.Equal(1, usage.TotalRequests);
        Assert.Equal(5, usage.TotalInputCharacters);
        Assert.Equal(1, usage.SuccessfulRequests);
        Assert.Equal(0, usage.InProgressRequests);
        Assert.Equal(0, usage.TranslatorInputCharacters);
        Assert.Equal(100, usage.TotalModelInputTokens);
        Assert.Equal(20, usage.TotalModelOutputTokens);
        Assert.Equal(1, usage.ModelSuccessfulRequests);
        Assert.Equal(0, usage.ModelInProgressRequests);
    }

    [Fact]
    public async Task ResponsesTranslator_ReportsFailedAndCanceledRequests()
    {
        using var failedHttpClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":{\"code\":\"failed\",\"message\":\"failed\"}}"),
            })));
        var failedUsage = new ScreenTranslationUsage();
        var failedTranslator = new AzureOpenAITranslatorService(
            new AzureOpenAIResponsesClient("https://example.test", "key", "deployment", failedHttpClient),
            failedUsage);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failedTranslator.TranslateAsync("Failure"));

        using var canceledHttpClient = new HttpClient(new StubHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var canceledUsage = new ScreenTranslationUsage();
        var canceledTranslator = new AzureOpenAITranslatorService(
            new AzureOpenAIResponsesClient("https://example.test", "key", "deployment", canceledHttpClient),
            canceledUsage);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledTranslator.TranslateAsync("Cancel", cancellation.Token));

        Assert.Equal(1, failedUsage.TotalRequests);
        Assert.Equal(1, failedUsage.FailedRequests);
        Assert.Equal(0, failedUsage.InProgressRequests);
        Assert.Equal(1, canceledUsage.TotalRequests);
        Assert.Equal(1, canceledUsage.CanceledRequests);
        Assert.Equal(0, canceledUsage.InProgressRequests);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
