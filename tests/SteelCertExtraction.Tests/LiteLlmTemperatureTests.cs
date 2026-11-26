using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

/// <summary>Returns queued responses in order and records every request body.</summary>
public sealed class SequencedHttpHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

    public List<string> Bodies { get; } = new();

    public SequencedHttpHandler(params (HttpStatusCode, string)[] responses) =>
        _responses = new Queue<(HttpStatusCode, string)>(responses);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
        if (_responses.Count == 0) throw new InvalidOperationException("Unexpected extra request.");
        var (status, body) = _responses.Dequeue();
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}

public class LiteLlmTemperatureTests
{
    private const string Ok = """{"choices":[{"message":{"content":"done"}}]}""";
    private const string TempRejected =
        """{"error":{"message":"`temperature` is deprecated for this model.","type":"invalid_request_error"}}""";

    private static bool HasTemperature(string json) =>
        JsonDocument.Parse(json).RootElement.TryGetProperty("temperature", out _);

    private static LiteLlmChatClient Client(SequencedHttpHandler handler, bool sendTemperature = true) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://proxy.example/v1/") }, "m", sendTemperature);

    private static Task<string> Call(LiteLlmChatClient c) => c.SendMultimodalAsync("s", "u", new List<byte[]>());

    [Fact]
    public async Task Default_sends_temperature_zero()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.OK, Ok));

        await Call(Client(handler));

        Assert.Equal(0, JsonDocument.Parse(handler.Bodies.Single()).RootElement.GetProperty("temperature").GetInt32());
    }

    [Fact]
    public async Task SendTemperature_false_omits_the_field()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.OK, Ok));

        await Call(Client(handler, sendTemperature: false));

        Assert.False(HasTemperature(handler.Bodies.Single()));
    }

    [Fact]
    public async Task Temperature_rejection_retries_once_without_it_and_succeeds()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.BadRequest, TempRejected), (HttpStatusCode.OK, Ok));

        var result = await Call(Client(handler));

        Assert.Equal("done", result);
        Assert.Equal(2, handler.Bodies.Count);
        Assert.True(HasTemperature(handler.Bodies[0]));
        Assert.False(HasTemperature(handler.Bodies[1]));
        // The retry must carry the same payload otherwise (model, messages).
        Assert.Equal("m", JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("model").GetString());
        Assert.Equal(2, JsonDocument.Parse(handler.Bodies[1]).RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Temperature_detection_is_case_insensitive()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, """{"error":"Unsupported parameter: TEMPERATURE"}"""), (HttpStatusCode.OK, Ok));

        await Call(Client(handler));

        Assert.Equal(2, handler.Bodies.Count);
    }

    [Fact]
    public async Task Other_400s_are_not_retried()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.BadRequest, """{"error":"messages must not be empty"}"""));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => Call(Client(handler)));

        Assert.Single(handler.Bodies);
        Assert.Contains("BadRequest", ex.Message);
        Assert.Contains("messages must not be empty", ex.Message);
    }

    [Fact]
    public async Task Non_400_mentioning_temperature_is_not_retried()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.InternalServerError, TempRejected));

        await Assert.ThrowsAsync<LlmCallException>(() => Call(Client(handler)));

        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task After_a_fallback_later_calls_do_not_send_temperature()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, TempRejected), (HttpStatusCode.OK, Ok), (HttpStatusCode.OK, Ok));
        var client = Client(handler);

        await Call(client);
        await Call(client);

        Assert.Equal(3, handler.Bodies.Count); // 2 for the first call, then exactly 1 — no new failed attempt
        Assert.False(HasTemperature(handler.Bodies[2]));
    }

    [Fact]
    public async Task If_the_retry_also_fails_the_second_error_is_reported()
    {
        var handler = new SequencedHttpHandler(
            (HttpStatusCode.BadRequest, TempRejected), (HttpStatusCode.BadGateway, "upstream down"));

        var ex = await Assert.ThrowsAsync<LlmCallException>(() => Call(Client(handler)));

        Assert.Equal(2, handler.Bodies.Count);
        Assert.Contains("BadGateway", ex.Message);
        Assert.Contains("upstream down", ex.Message);
    }

    [Fact]
    public async Task Temperature_error_when_temperature_was_never_sent_is_not_retried()
    {
        var handler = new SequencedHttpHandler((HttpStatusCode.BadRequest, TempRejected));

        await Assert.ThrowsAsync<LlmCallException>(() => Call(Client(handler, sendTemperature: false)));

        Assert.Single(handler.Bodies);
    }

    // --- configuration ---

    private static LlmOptions ResolveOptions(params (string Key, string? Value)[] pairs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCertExtraction(config);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<LlmOptions>();
    }

    [Fact]
    public void SendTemperature_defaults_to_true()
    {
        var options = ResolveOptions(("Llm:LiteLlm:BaseUrl", "https://p/v1/"), ("Llm:LiteLlm:ModelId", "m"));

        Assert.True(options.LiteLlm.SendTemperature);
    }

    [Fact]
    public void SendTemperature_false_is_read_from_configuration()
    {
        var options = ResolveOptions(
            ("Llm:LiteLlm:BaseUrl", "https://p/v1/"), ("Llm:LiteLlm:ModelId", "m"),
            ("Llm:LiteLlm:SendTemperature", "false"));

        Assert.False(options.LiteLlm.SendTemperature);
    }
}
