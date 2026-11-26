using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelCertExtraction.Services;

/// <summary>
/// Calls Claude directly through Anthropic's native Messages API
/// (POST v1/messages). Used for personal/dev setups without a LiteLLM proxy.
///
/// The caller's DI registration must set HttpClient.BaseAddress (with a
/// trailing slash, e.g. https://api.anthropic.com/). The request path is
/// relative so any base path is preserved.
/// </summary>
public class AnthropicChatClient : ILlmChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;
    private readonly string _apiKey;
    private readonly string _apiVersion;

    public string ModelIdentifier => _modelId;

    public AnthropicChatClient(HttpClient httpClient, string modelId, string apiKey, string apiVersion)
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _apiKey = apiKey;
        _apiVersion = apiVersion;
    }

    public async Task<string> SendMultimodalAsync(
        string systemPrompt,
        string userPrompt,
        List<byte[]> images,
        CancellationToken ct = default)
    {
        var contentBlocks = new List<object>
        {
            new { type = "text", text = userPrompt },
        };

        foreach (var imageBytes in images)
        {
            contentBlocks.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = "image/png",
                    data = Convert.ToBase64String(imageBytes),
                },
            });
        }

        var requestBody = new
        {
            model = _modelId,
            max_tokens = 8000,
            // No temperature: newer Claude models reject it as deprecated
            // (400 invalid_request_error), so use the model's default sampling.
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = contentBlocks },
            },
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", _apiVersion);

        using var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new LlmCallException(
                $"Anthropic call failed with {response.StatusCode}: {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<MessagesResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var text = string.Concat(
            (parsed?.Content ?? new List<ContentBlock>())
                .Where(b => b.Type == "text")
                .Select(b => b.Text));

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new LlmCallException(
                $"Anthropic response had no text content. Raw response: {responseJson}");
        }

        return text;
    }

    // --- Minimal response shape ---

    private class MessagesResponse
    {
        [JsonPropertyName("content")] public List<ContentBlock>? Content { get; set; }
    }

    private class ContentBlock
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }
}
