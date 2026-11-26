using SkiaSharp;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class LlmConnectionCheckTests
{
    private sealed class FakeClient(Func<List<byte[]>, Task<string>> behavior, string model = "test-model") : ILlmChatClient
    {
        public string ModelIdentifier => model;
        public List<byte[]>? SeenImages { get; private set; }
        public string? SeenUserPrompt { get; private set; }

        public Task<string> SendMultimodalAsync(
            string systemPrompt, string userPrompt, List<byte[]> images, CancellationToken ct = default)
        {
            SeenImages = images;
            SeenUserPrompt = userPrompt;
            return behavior(images);
        }
    }

    private static LlmOptions LiteLlmOptions(string? apiKey = "sk-secret-123") => new()
    {
        Provider = LlmProvider.LiteLlm,
        LiteLlm = { BaseUrl = "https://proxy.example/v1/", ApiKey = apiKey, ModelId = "test-model" },
    };

    private static async Task<(int Code, string Output)> Run(ILlmChatClient client, LlmOptions options, CancellationToken ct = default)
    {
        var writer = new StringWriter();
        var code = await LlmConnectionCheck.RunAsync(client, options, writer, ct);
        return (code, writer.ToString());
    }

    [Fact]
    public async Task Success_returns_0_and_prints_provider_url_model_and_reply()
    {
        var (code, output) = await Run(new FakeClient(_ => Task.FromResult("OK")), LiteLlmOptions());

        Assert.Equal(0, code);
        Assert.Contains("Provider : LiteLlm", output);
        Assert.Contains("https://proxy.example/v1/", output);
        Assert.Contains("Model    : test-model", output);
        Assert.Contains("Result   : OK", output);
        Assert.Contains("Reply    : OK", output);
    }

    [Fact]
    public async Task Sends_a_text_prompt_and_exactly_one_valid_1x1_PNG()
    {
        var client = new FakeClient(_ => Task.FromResult("OK"));

        await Run(client, LiteLlmOptions());

        Assert.False(string.IsNullOrWhiteSpace(client.SeenUserPrompt));
        var image = Assert.Single(client.SeenImages!);
        using var bitmap = SKBitmap.Decode(image);
        Assert.NotNull(bitmap);
        Assert.Equal(1, bitmap.Width);
        Assert.Equal(1, bitmap.Height);
    }

    [Fact]
    public async Task Failure_returns_1_with_the_error_text()
    {
        var client = new FakeClient(_ => throw new LlmCallException("LiteLLM call failed with BadGateway: upstream down"));

        var (code, output) = await Run(client, LiteLlmOptions());

        Assert.Equal(1, code);
        Assert.Contains("FAILED", output);
        Assert.Contains("BadGateway", output);
        Assert.Contains("upstream down", output);
    }

    [Fact]
    public async Task API_key_is_never_printed_even_when_an_error_body_echoes_it()
    {
        var client = new FakeClient(_ =>
            throw new LlmCallException("LiteLLM call failed with Unauthorized: invalid key 'sk-secret-123'"));

        var (_, output) = await Run(client, LiteLlmOptions("sk-secret-123"));

        Assert.DoesNotContain("sk-secret-123", output);
        Assert.Contains("***", output);
        Assert.Contains("configured (not shown)", output);
    }

    [Fact]
    public async Task API_key_is_masked_in_a_successful_reply_too()
    {
        var (code, output) = await Run(new FakeClient(_ => Task.FromResult("echo sk-secret-123")), LiteLlmOptions("sk-secret-123"));

        Assert.Equal(0, code);
        Assert.DoesNotContain("sk-secret-123", output);
    }

    [Fact]
    public async Task Anthropic_key_is_masked_too()
    {
        var options = new LlmOptions { Provider = LlmProvider.Anthropic, Anthropic = { ApiKey = "sk-ant-abc" } };
        var client = new FakeClient(_ => throw new LlmCallException("Anthropic call failed with Unauthorized: key sk-ant-abc"));

        var (code, output) = await Run(client, options);

        Assert.Equal(1, code);
        Assert.DoesNotContain("sk-ant-abc", output);
        Assert.Contains("Provider : Anthropic", output);
        Assert.Contains("https://api.anthropic.com/", output);
    }

    [Fact]
    public async Task No_key_configured_is_reported_without_printing_anything_secret()
    {
        var (_, output) = await Run(new FakeClient(_ => Task.FromResult("OK")), LiteLlmOptions(apiKey: null));

        Assert.Contains("none configured", output);
    }

    [Theory]
    [InlineData("LiteLLM call failed with NotFound: no route", "BaseUrl")]
    [InlineData("LiteLLM call failed with Unauthorized: bad key", "ApiKey")]
    [InlineData("LiteLLM call failed with BadRequest: `temperature` is deprecated", "SendTemperature")]
    [InlineData("LiteLLM call failed with BadRequest: image input not supported", "image")]
    public async Task Common_failures_get_a_hint(string errorMessage, string expectedHintFragment)
    {
        var client = new FakeClient(_ => throw new LlmCallException(errorMessage));

        var (_, output) = await Run(client, LiteLlmOptions());

        var hintLine = output.Split('\n').FirstOrDefault(l => l.Contains("Hint"));
        Assert.NotNull(hintLine);
        Assert.Contains(expectedHintFragment, hintLine);
    }

    [Fact]
    public async Task Unreachable_host_gets_a_network_hint()
    {
        var client = new FakeClient(_ => throw new HttpRequestException("No such host is known."));

        var (code, output) = await Run(client, LiteLlmOptions());

        Assert.Equal(1, code);
        Assert.Contains("Could not reach the host", output);
    }

    [Fact]
    public async Task Cancellation_is_reported_as_a_timeout_not_a_crash()
    {
        using var cts = new CancellationTokenSource();
        var client = new FakeClient(_ => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var (code, output) = await Run(client, LiteLlmOptions(), cts.Token);

        Assert.Equal(1, code);
        Assert.Contains("timed out", output);
    }

    [Fact]
    public async Task Very_long_replies_are_truncated()
    {
        var (_, output) = await Run(new FakeClient(_ => Task.FromResult(new string('x', 5000))), LiteLlmOptions());

        Assert.True(output.Length < 1500);
        Assert.Contains("…", output);
    }
}
