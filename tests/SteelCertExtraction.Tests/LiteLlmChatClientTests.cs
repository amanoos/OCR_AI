using System.Net;
using System.Text.Json;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class LiteLlmChatClientTests
{
    private const string OkResponse = """{"choices":[{"message":{"content":"{\"certificates\":[]}"}}]}""";

    private static (LiteLlmChatClient Client, FakeHttpHandler Handler) Create(
        string response, HttpStatusCode status = HttpStatusCode.OK, string baseUrl = "https://proxy.example/v1/")
    {
        var handler = new FakeHttpHandler(response, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        return (new LiteLlmChatClient(http, "test-model"), handler);
    }

    [Fact]
    public async Task Posts_to_chat_completions_preserving_base_path()
    {
        // Regression: a leading slash on the request path dropped the "/v1/" base path.
        var (client, handler) = Create(OkResponse);

        await client.SendMultimodalAsync("sys", "user", new List<byte[]>());

        Assert.Equal("https://proxy.example/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
    }

    [Fact]
    public async Task Sends_system_message_text_part_and_image_data_uris()
    {
        var (client, handler) = Create(OkResponse);
        var png = new byte[] { 1, 2, 3 };

        await client.SendMultimodalAsync("sys prompt", "user prompt", new List<byte[]> { png, png });

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var root = doc.RootElement;
        Assert.Equal("test-model", root.GetProperty("model").GetString());
        Assert.Equal(0, root.GetProperty("temperature").GetInt32());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("sys prompt", messages[0].GetProperty("content").GetString());

        var parts = messages[1].GetProperty("content");
        Assert.Equal(3, parts.GetArrayLength());
        Assert.Equal("text", parts[0].GetProperty("type").GetString());
        Assert.Equal("user prompt", parts[0].GetProperty("text").GetString());
        Assert.Equal("image_url", parts[1].GetProperty("type").GetString());
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(png)}",
            parts[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task Returns_message_content()
    {
        var (client, _) = Create(OkResponse);

        var result = await client.SendMultimodalAsync("s", "u", new List<byte[]>());

        Assert.Equal("""{"certificates":[]}""", result);
    }

    [Fact]
    public async Task Non_success_status_throws_with_status_and_body()
    {
        var (client, _) = Create("boom", HttpStatusCode.BadGateway);

        var ex = await Assert.ThrowsAsync<LlmCallException>(
            () => client.SendMultimodalAsync("s", "u", new List<byte[]>()));

        Assert.Contains("BadGateway", ex.Message);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task Empty_content_throws()
    {
        var (client, _) = Create("""{"choices":[{"message":{"content":""}}]}""");

        await Assert.ThrowsAsync<LlmCallException>(
            () => client.SendMultimodalAsync("s", "u", new List<byte[]>()));
    }

    [Fact]
    public void ModelIdentifier_returns_configured_model()
    {
        var (client, _) = Create(OkResponse);
        Assert.Equal("test-model", client.ModelIdentifier);
    }
}
