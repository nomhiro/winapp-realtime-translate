using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using RealtimeTranslate.Config;
using RealtimeTranslate.Llm;

namespace RealtimeTranslate.Tests;

public class AzureOpenAIProfileTests
{
    [Fact]
    public void Profiles_CoverDocumentedGpt5AndGpt6Identifiers()
    {
        string[] expected =
        {
            "gpt-4.1",
            "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5",
            "gpt-5.4-nano", "gpt-5.4-mini", "gpt-5.4-pro", "gpt-5.4",
            "gpt-5.3-codex", "gpt-5.2-codex", "gpt-5.2", "gpt-5.1-codex-max",
            "gpt-5.1", "gpt-5.1-chat", "gpt-5.1-codex", "gpt-5.1-codex-mini",
            "gpt-5-pro", "gpt-5-codex", "gpt-5", "gpt-5-mini", "gpt-5-nano",
            "gpt-6-astra", "gpt-6-sol", "gpt-6-luna", "gpt-6.1-sol", "gpt-6.1-terra",
        };

        Assert.Equal(expected, AzureOpenAIModelProfiles.All.Select(AzureOpenAIModelProfiles.GetModelId));
        Assert.False(AzureOpenAIModelProfiles.UsesResponsesApi(AzureOpenAIModelProfile.Gpt41));
        Assert.All(
            AzureOpenAIModelProfiles.All.Where(profile => profile != AzureOpenAIModelProfile.Gpt41),
            profile => Assert.True(AzureOpenAIModelProfiles.UsesResponsesApi(profile)));
    }

    [Fact]
    public void MissingProfileInSavedSettings_KeepsGpt41Default()
    {
        AppSettings settings = JsonSerializer.Deserialize<AppSettings>(
            """{"OpenAIEndpoint":"https://example.openai.azure.com","OpenAIKey":"key","OpenAIDeployment":"custom-name"}""")!;

        Assert.Equal(AzureOpenAIModelProfile.Gpt41, settings.OpenAIModelProfile);
        Assert.Equal("custom-name", settings.OpenAIDeployment);
    }

    [Fact]
    public async Task ResponsesStream_YieldsOnlyOutputTextAndTargetsConfiguredDeployment()
    {
        const string events =
            "event: response.output_item.added\n" +
            "data: {\"type\":\"response.output_item.added\",\"item\":{\"type\":\"reasoning\",\"status\":\"in_progress\"}}\n\n" +
            "event: response.output_text.delta\n" +
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"日本語\"}\n\n" +
            "event: response.completed\n" +
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n";
        var handler = new StubHttpHandler(events);
        using var httpClient = new HttpClient(handler);
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com/openai/v1/",
            "test-key",
            "deployment-alias",
            httpClient);

        var result = new StringBuilder();
        await foreach (string delta in service.StreamTextAsync("system instruction", "user input"))
        {
            result.Append(delta);
        }

        Assert.Equal("日本語", result.ToString());
        Assert.Equal("https://example.openai.azure.com/openai/v1/responses", handler.RequestUri?.ToString());
        Assert.Equal("test-key", handler.ApiKey);
        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("deployment-alias", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("system instruction", body.RootElement.GetProperty("instructions").GetString());
        Assert.Equal(8192, body.RootElement.GetProperty("max_output_tokens").GetInt32());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("temperature", out _));
        Assert.False(body.RootElement.TryGetProperty("reasoning_effort", out _));
    }

    [Fact]
    public async Task ResponsesStream_FailedEventSurfacesModelError()
    {
        const string events =
            "event: response.failed\n" +
            "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"deployment_not_found\",\"message\":\"The configured deployment was not found.\"}}}\n\n";
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(new StubHttpHandler(events)));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (string _ in service.StreamTextAsync("instructions", "input"))
            {
            }
        });

        Assert.Contains("deployment_not_found", error.Message);
        Assert.Contains("configured deployment was not found", error.Message);
    }

    [Fact]
    public async Task ResponsesHttpError_ExplainsLikelyConfigurationIssue()
    {
        var handler = new StubHttpHandler(
            """{"error":{"code":"unsupported_api","message":"Responses API is not available for this deployment."}}""",
            HttpStatusCode.BadRequest);
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(handler));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (string _ in service.StreamTextAsync("instructions", "input"))
            {
            }
        });

        Assert.Contains("400 BadRequest", error.Message);
        Assert.Contains("unsupported_api", error.Message);
        Assert.Contains("Responses API is not available", error.Message);
    }

    [Fact]
    public async Task ResponsesStream_IncompleteResponseExplainsTokenLimit()
    {
        const string events =
            "event: response.completed\n" +
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"incomplete\",\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}\n\n";
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(new StubHttpHandler(events)));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (string _ in service.StreamTextAsync("instructions", "input"))
            {
            }
        });

        Assert.Contains("incomplete response (max_output_tokens)", error.Message);
        Assert.Contains("8192 tokens", error.Message);
    }

    [Fact]
    public async Task ResponsesStream_EmptyCompletedResponseYieldsNoText()
    {
        const string events =
            "event: response.completed\n" +
            "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\"}}\n\n";
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(new StubHttpHandler(events)));

        var deltas = new List<string>();
        await foreach (string delta in service.StreamTextAsync("instructions", "input"))
        {
            deltas.Add(delta);
        }

        Assert.Empty(deltas);
    }

    [Fact]
    public async Task ResponsesStream_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new StubHttpHandler("");
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(handler));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (string _ in service.StreamTextAsync("instructions", "input", cancellation.Token))
            {
            }
        });

        Assert.True(handler.CancellationObserved);
    }

    [Fact]
    public async Task ResponsesStream_InterruptedStreamFailsInsteadOfReturningPartialSuccess()
    {
        const string events =
            "event: response.output_text.delta\n" +
            "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n";
        var service = new AzureOpenAIResponsesClient(
            "https://example.openai.azure.com",
            "test-key",
            "deployment-alias",
            new HttpClient(new StubHttpHandler(events)));

        var deltas = new List<string>();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (string delta in service.StreamTextAsync("instructions", "input"))
            {
                deltas.Add(delta);
            }
        });

        Assert.Equal(new[] { "partial" }, deltas);
        Assert.Contains("before a response.completed event", error.Message);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _statusCode;

        public Uri? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? RequestBody { get; private set; }
        public bool CancellationObserved { get; private set; }

        public StubHttpHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _body = body;
            _statusCode = statusCode;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CancellationObserved = cancellationToken.IsCancellationRequested;
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_body, Encoding.UTF8, "text/event-stream"),
            };
            return response;
        }
    }
}
