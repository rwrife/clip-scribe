using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClipScribe.Core.Abstractions;
using ClipScribe.Core.Models;

namespace ClipScribe.Core.Services;

public sealed class LocalAiTextTransformClient : ILocalAiTextTransformClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public LocalAiTextTransformClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<string> TransformAsync(
        LocalAiSettings settings,
        string transformInstruction,
        string input,
        CancellationToken cancellationToken = default)
    {
        var normalized = LocalAiSettings.Normalize(settings);

        if (!normalized.IsEnabledAndConfigured)
        {
            throw new InvalidOperationException("Local AI transforms are disabled or not configured.");
        }

        if (string.IsNullOrWhiteSpace(transformInstruction))
        {
            throw new ArgumentException("Transform instruction is required.", nameof(transformInstruction));
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input text is required.", nameof(input));
        }

        var endpointUri = BuildChatCompletionsUri(normalized.Endpoint);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

        var payload = new ChatCompletionsRequest(
            Model: normalized.Model,
            Messages:
            [
                new ChatMessage("system", "You are a precise text transformation assistant. Return only transformed text."),
                new ChatMessage("user", $"Transform request: {transformInstruction}\n\nInput text:\n{input}")
            ],
            Temperature: 0.1);

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpointUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Local AI endpoint returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var transformed = TryExtractResponseContent(body);
        if (string.IsNullOrWhiteSpace(transformed))
        {
            throw new InvalidOperationException("Local AI endpoint returned an empty transform response.");
        }

        return transformed.Trim();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static Uri BuildChatCompletionsUri(string endpoint)
    {
        var baseEndpoint = endpoint.Trim().TrimEnd('/');

        var requestUri = baseEndpoint.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseEndpoint
            : baseEndpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? $"{baseEndpoint}/chat/completions"
                : $"{baseEndpoint}/v1/chat/completions";

        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Local AI endpoint must be a valid http(s) URL.");
        }

        return uri;
    }

    private static string? TryExtractResponseContent(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var first = choices[0];
        if (!first.TryGetProperty("message", out var message))
        {
            return null;
        }

        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (item.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    var value = text.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parts.Add(value);
                    }
                }
            }

            return parts.Count == 0 ? null : string.Join(string.Empty, parts);
        }

        return null;
    }

    private sealed record ChatCompletionsRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        double Temperature);

    private sealed record ChatMessage(string Role, string Content);
}
