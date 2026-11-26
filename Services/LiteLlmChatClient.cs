using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteelCertExtraction.Services;

/// <summary>
/// Calls Claude through your LiteLLM proxy (which routes to Bedrock) using
/// the OpenAI-compatible chat completions shape that LiteLLM exposes.
///
/// This assumes the same base URL / auth your experiment tracker's
/// streaming-summary feature already uses — adjust the config keys to
/// match whatever you've named them there, ideally reusing the same
/// HttpClient registration rather than creating a second one.
/// </summary>
public class LiteLlmChatClient : ILlmChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _modelId;

    // Mutable on purpose: flips to false for this client's lifetime if the
    // proxy/model rejects "temperature" (see SendMultimodalAsync).
    private bool _sendTemperature;

    public string ModelIdentifier => _modelId;

    public LiteLlmChatClient(HttpClient httpClient, string modelId, bool sendTemperature = true)
    {
        _httpClient = httpClient;
        _modelId = modelId;
        _sendTemperature = sendTemperature;
        // Expect the caller's DI registration to have already set BaseAddress
        // and the Authorization header for the LiteLLM proxy — same as
        // whatever's used for the experiment summary streaming calls.
    }

    public async Task<string> SendMultimodalAsync(
        string systemPrompt,
        string userPrompt,
        List<byte[]> images,
        CancellationToken ct = default)
    {
        // LiteLLM's OpenAI-compatible endpoint expects image content as
        // data URIs inside a content-parts array, same shape as calling
        // OpenAI's vision API directly — LiteLLM translates this into the
        // Bedrock-native Converse API call format under the hood.
        var contentParts = new List<object>
        {
            new { type = "text", text = userPrompt },
        };

        foreach (var imageBytes in images)
        {
            var base64 = Convert.ToBase64String(imageBytes);
            contentParts.Add(new
            {
                type = "image_url",
                image_url = new { url = $"data:image/png;base64,{base64}" },
            });
        }

        string BuildJson()
        {
            var requestBody = new Dictionary<string, object>
            {
                ["model"] = _modelId,
                ["max_tokens"] = 8000, // cert PDFs with multiple certs + chemistry tables need headroom
                ["messages"] = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = contentParts },
                },
            };
            if (_sendTemperature)
            {
                requestBody["temperature"] = 0; // deterministic extraction, not creative generation
            }
            return JsonSerializer.Serialize(requestBody);
        }

        var result = await PostAsync(BuildJson(), ct);

        // Newer Claude models reject "temperature" with a 400 (direct Anthropic did).
        // If that's what this is, retry ONCE without it and stop sending it from
        // now on. Any other failure falls through to the normal error below.
        if (!result.Success
            && _sendTemperature
            && result.Status == HttpStatusCode.BadRequest
            && result.Body.Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            _sendTemperature = false;
            result = await PostAsync(BuildJson(), ct);
        }

        if (!result.Success)
        {
            throw new LlmCallException(
                $"LiteLLM call failed with {result.Status}: {result.Body}");
        }

        var responseJson = result.Body;
        var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(responseJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new LlmCallException(
                $"LiteLLM response had no content. Raw response: {responseJson}");
        }

        return content;
    }

    private async Task<(bool Success, HttpStatusCode Status, string Body)> PostAsync(
        string json, CancellationToken ct)
    {
        // Relative path: keeps the BaseAddress path (e.g. /v1/).
        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        using var response = await _httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return (response.IsSuccessStatusCode, response.StatusCode, body);
    }

    // --- Minimal response shape, just enough to pull out message content ---

    private class ChatCompletionResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")] public Message? Message { get; set; }
    }

    private class Message
    {
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}

// DI registration lives in ServiceCollectionExtensions.AddCertExtraction.
