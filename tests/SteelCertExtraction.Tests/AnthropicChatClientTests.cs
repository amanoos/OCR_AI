using System.Net;
using System.Text.Json;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class AnthropicChatClientTests
{
    private const string OkResponse =
        """{"content":[{"type":"text","text":"{\"certificates\":"},{"type":"text","text":"[]}"}]}""";

    private static (AnthropicChatClient Client, FakeHttpHandler Handler) Create(
        string response, HttpStatusCode status = HttpStatusCode.OK, string baseUrl = "https://api.anthropic.com/")
    {
        var handler = new FakeHttpHandler(response, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        return (new AnthropicChatClient(http, "claude-test", "sk-test", "2023-06-01"), handler);
    }

    [Fact]
    public async Task Posts_to_v1_messages_with_auth_headers()
    {
        var (client, handler) = Create(OkResponse);

        await client.SendMultimodalAsync("sys", "user", new List<byte[]>());

        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("sk-test", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task Preserves_base_path_for_gateways()
    {
        var (client, handler) = Create(OkResponse, baseUrl: "https://gateway.example/anthropic/");

        await client.SendMultimodalAsync("s", "u", new List<byte[]>());

        Assert.Equal("https://gateway.example/anthropic/v1/messages", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Body_has_top_level_system_text_block_and_base64_image_blocks()
    {
        var (client, handler) = Create(OkResponse);
        var png = new byte[] { 9, 8, 7 };

        await client.SendMultimodalAsync("sys prompt", "user prompt", new List<byte[]> { png });

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;
        Assert.Equal("claude-test", root.GetProperty("model").GetString());
        Assert.Equal(8000, root.GetProperty("max_tokens").GetInt32());
        // Newer Claude models reject `temperature` as deprecated.
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.Equal("sys prompt", root.GetProperty("system").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());

        var blocks = messages[0].GetProperty("content");
        Assert.Equal(2, blocks.GetArrayLength());
        Assert.Equal("text", blocks[0].GetProperty("type").GetString());
        Assert.Equal("user prompt", blocks[0].GetProperty("text").GetString());

        var image = blocks[1];
        Assert.Equal("image", image.GetProperty("type").GetString());
        var source = image.GetProperty("source");
        Assert.Equal("base64", source.GetProperty("type").GetString());
        Assert.Equal("image/png", source.GetProperty("media_type").GetString());
        Assert.Equal(Convert.ToBase64String(png), source.GetProperty("data").GetString());
    }

    [Fact]
    public async Task Concatenates_text_blocks_and_ignores_other_block_types()
    {
        var (client, _) = Create(
            """{"content":[{"type":"thinking","thinking":"x"},{"type":"text","text":"a"},{"type":"text","text":"b"}]}""");

        var result = await client.SendMultimodalAsync("s", "u", new List<byte[]>());

        Assert.Equal("ab", result);
    }

    [Fact]
    public async Task Non_success_status_throws_with_status_and_body()
    {
        var (client, _) = Create("""{"error":"overloaded"}""", HttpStatusCode.TooManyRequests);

        var ex = await Assert.ThrowsAsync<LlmCallException>(
            () => client.SendMultimodalAsync("s", "u", new List<byte[]>()));

        Assert.Contains("TooManyRequests", ex.Message);
        Assert.Contains("overloaded", ex.Message);
    }

    [Fact]
    public async Task No_text_content_throws()
    {
        var (client, _) = Create("""{"content":[]}""");

        await Assert.ThrowsAsync<LlmCallException>(
            () => client.SendMultimodalAsync("s", "u", new List<byte[]>()));
    }

    [Fact]
    public void ModelIdentifier_returns_configured_model()
    {
        var (client, _) = Create(OkResponse);
        Assert.Equal("claude-test", client.ModelIdentifier);
    }
}
