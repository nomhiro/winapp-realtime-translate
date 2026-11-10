using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RealtimeTranslate.Llm;

internal sealed class AzureOpenAIResponsesClient
{
    private const int MaxOutputTokens = 8192;
    private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly HttpClient _httpClient;
    private readonly Uri _responsesUri;
    private readonly string _key;
    private readonly string _deployment;

    internal AzureOpenAIResponsesClient(
        string endpoint,
        string key,
        string deployment,
        HttpClient? httpClient = null)
    {
        _responsesUri = BuildResponsesUri(endpoint);
        _key = key;
        _deployment = deployment;
        _httpClient = httpClient ?? SharedHttpClient;
    }

    internal async IAsyncEnumerable<string> StreamTextAsync(
        string instructions,
        string input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        Action<int, int>? usageCallback = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _responsesUri);
        request.Headers.Add("api-key", _key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = JsonContent.Create(new Dictionary<string, object>
        {
            ["model"] = _deployment,
            ["instructions"] = instructions,
            ["input"] = input,
            ["max_output_tokens"] = MaxOutputTokens,
            ["stream"] = true,
        });

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw CreateHttpException(response.StatusCode, responseBody);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (string delta in ReadTextDeltasAsync(stream, cancellationToken, usageCallback).ConfigureAwait(false))
        {
            yield return delta;
        }
    }

    internal static Uri BuildResponsesUri(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Azure OpenAI endpoint must be an absolute HTTPS URL.");
        }

        var builder = new UriBuilder(uri);
        string path = builder.Path.TrimEnd('/');
        if (path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
        {
            // The endpoint already includes the v1 API root.
        }
        else if (path.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
        {
            path += "/v1";
        }
        else
        {
            path += "/openai/v1";
        }

        builder.Path = path + "/responses";
        return builder.Uri;
    }

    internal static async IAsyncEnumerable<string> ReadTextDeltasAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        Action<int, int>? usageCallback = null)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var data = new StringBuilder();
        string? eventName = null;
        bool completed = false;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    StreamEvent parsed = ParseStreamEvent(eventName, data.ToString());
                    if (parsed.Error is not null)
                    {
                        throw new InvalidOperationException(parsed.Error);
                    }

                    if (parsed.InputTokens is int inputTokens && parsed.OutputTokens is int outputTokens)
                    {
                        usageCallback?.Invoke(inputTokens, outputTokens);
                    }

                    completed |= parsed.Completed;
                    if (!string.IsNullOrEmpty(parsed.TextDelta))
                    {
                        yield return parsed.TextDelta;
                    }
                }

                eventName = null;
                data.Clear();
            }
            else if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line["data:".Length..].TrimStart());
            }
        }

        if (data.Length > 0)
        {
            StreamEvent parsed = ParseStreamEvent(eventName, data.ToString());
            if (parsed.Error is not null)
            {
                throw new InvalidOperationException(parsed.Error);
            }

            if (parsed.InputTokens is int inputTokens && parsed.OutputTokens is int outputTokens)
            {
                usageCallback?.Invoke(inputTokens, outputTokens);
            }

            completed |= parsed.Completed;
            if (!string.IsNullOrEmpty(parsed.TextDelta))
            {
                yield return parsed.TextDelta;
            }
        }

        if (!completed)
        {
            throw new InvalidOperationException(
                "Azure OpenAI Responses stream ended before a response.completed event was received.");
        }
    }

    private static StreamEvent ParseStreamEvent(string? eventName, string data)
    {
        if (data == "[DONE]")
        {
            return new StreamEvent(null, true, null, null, null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            JsonElement root = document.RootElement;
            string type = GetString(root, "type") ?? eventName ?? string.Empty;

            if (type == "response.output_text.delta")
            {
                return new StreamEvent(GetString(root, "delta"), false, null, null, null);
            }

            if (type is "error" or "response.failed")
            {
                JsonElement response = GetNested(root, "response");
                JsonElement error = GetNested(root, "response", "error");
                if (error.ValueKind == JsonValueKind.Undefined)
                {
                    error = GetNested(root, "error");
                }

                string code = GetString(error, "code") ?? "request_failed";
                string message = GetString(error, "message") ?? "The model request failed.";
                (int? input, int? output) = ReadUsage(GetNested(response, "usage"));
                return new StreamEvent(
                    null, false, $"Azure OpenAI Responses API error ({code}): {message}", input, output);
            }

            if (type == "response.incomplete")
            {
                JsonElement response = GetNested(root, "response");
                (int? input, int? output) = ReadUsage(GetNested(response, "usage"));
                return new StreamEvent(null, false, GetIncompleteMessage(response), input, output);
            }

            if (type == "response.completed")
            {
                JsonElement response = GetNested(root, "response");
                (int? input, int? output) = ReadUsage(GetNested(response, "usage"));
                string? status = GetString(response, "status");
                if (status == "incomplete")
                {
                    return new StreamEvent(null, false, GetIncompleteMessage(response), input, output);
                }

                if (status == "failed")
                {
                    JsonElement error = GetNested(response, "error");
                    string code = GetString(error, "code") ?? "request_failed";
                    string message = GetString(error, "message") ?? "The model request failed.";
                    return new StreamEvent(
                        null, false, $"Azure OpenAI Responses API error ({code}): {message}", input, output);
                }

                if (status == "cancelled")
                {
                    return new StreamEvent(
                        null, false,
                        "Azure OpenAI Responses API request was cancelled before completion.",
                        input,
                        output);
                }

                return new StreamEvent(null, true, null, input, output);
            }

            return new StreamEvent(null, false, null, null, null);
        }
        catch (JsonException)
        {
            return new StreamEvent(null, false, "Azure OpenAI Responses API returned an invalid streaming event.", null, null);
        }
    }

    private static (int? Input, int? Output) ReadUsage(JsonElement usage)
    {
        int? input = GetInt32(usage, "input_tokens");
        int? output = GetInt32(usage, "output_tokens");
        return input is not null && output is not null ? (input, output) : (null, null);
    }

    private static int? GetInt32(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out JsonElement value) &&
           value.TryGetInt32(out int count)
            ? count
            : null;

    private static InvalidOperationException CreateHttpException(
        System.Net.HttpStatusCode statusCode,
        string responseBody)
    {
        string? message = null;
        string? code = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement error = GetNested(document.RootElement, "error");
            message = GetString(error, "message");
            code = GetString(error, "code");
        }
        catch (JsonException)
        {
        }

        string status = $"{(int)statusCode} {statusCode}";
        string details = string.IsNullOrWhiteSpace(message)
            ? "Verify the Azure OpenAI endpoint, deployment, and API support for the selected model profile."
            : $"{(string.IsNullOrWhiteSpace(code) ? "" : $"{code}: ")}{message}";
        return new InvalidOperationException($"Azure OpenAI Responses API request failed ({status}). {details}");
    }

    private static string GetIncompleteMessage(JsonElement response)
    {
        string? reason = GetString(GetNested(response, "incomplete_details"), "reason");
        return string.IsNullOrWhiteSpace(reason)
            ? "Azure OpenAI Responses API returned an incomplete response. The configured output token limit may have been reached."
            : $"Azure OpenAI Responses API returned an incomplete response ({reason}). The configured output token limit is 8192 tokens.";
    }

    private static JsonElement GetNested(JsonElement element, params string[] properties)
    {
        foreach (string property in properties)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(property, out element))
            {
                return default;
            }
        }

        return element;
    }

    private static string? GetString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private readonly record struct StreamEvent(
        string? TextDelta,
        bool Completed,
        string? Error,
        int? InputTokens,
        int? OutputTokens);
}
