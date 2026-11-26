using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class ProviderSelectionTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.ToDictionary(p => p.Key, p => p.Value))
            .Build();

    private static ILlmChatClient Resolve(IConfiguration config)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCertExtraction(config);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ILlmChatClient>();
    }

    [Fact]
    public void LiteLlm_provider_resolves_LiteLlmChatClient()
    {
        var client = Resolve(Config(
            ("Llm:Provider", "LiteLlm"),
            ("Llm:LiteLlm:BaseUrl", "https://proxy/v1/"),
            ("Llm:LiteLlm:ModelId", "bedrock-claude")));

        Assert.IsType<LiteLlmChatClient>(client);
        Assert.Equal("bedrock-claude", client.ModelIdentifier);
    }

    [Fact]
    public void Anthropic_provider_resolves_AnthropicChatClient_with_default_model()
    {
        var client = Resolve(Config(
            ("Llm:Provider", "Anthropic"),
            ("Llm:Anthropic:ApiKey", "sk-test")));

        Assert.IsType<AnthropicChatClient>(client);
        Assert.Equal(new AnthropicOptions().ModelId, client.ModelIdentifier);
    }

    [Fact]
    public void Provider_name_is_case_insensitive()
    {
        var client = Resolve(Config(
            ("Llm:Provider", "anthropic"),
            ("Llm:Anthropic:ApiKey", "sk-test")));

        Assert.IsType<AnthropicChatClient>(client);
    }

    [Fact]
    public void Provider_defaults_to_LiteLlm_when_unset()
    {
        var client = Resolve(Config(
            ("Llm:LiteLlm:BaseUrl", "https://proxy/v1/"),
            ("Llm:LiteLlm:ModelId", "m")));

        Assert.IsType<LiteLlmChatClient>(client);
    }

    [Fact]
    public void Legacy_LiteLlm_keys_still_work()
    {
        var client = Resolve(Config(
            ("LiteLlm:BaseUrl", "https://proxy/v1/"),
            ("LiteLlm:ClaudeModelId", "legacy-model")));

        Assert.IsType<LiteLlmChatClient>(client);
        Assert.Equal("legacy-model", client.ModelIdentifier);
    }

    [Fact]
    public void New_keys_take_precedence_over_legacy()
    {
        var client = Resolve(Config(
            ("Llm:LiteLlm:BaseUrl", "https://new/v1/"),
            ("Llm:LiteLlm:ModelId", "new-model"),
            ("LiteLlm:BaseUrl", "https://old/v1/"),
            ("LiteLlm:ClaudeModelId", "old-model")));

        Assert.Equal("new-model", client.ModelIdentifier);
    }

    [Fact]
    public void Unknown_provider_throws_listing_valid_values()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(Config(("Llm:Provider", "Bogus"))));

        Assert.Contains("Bogus", ex.Message);
        Assert.Contains("Anthropic", ex.Message);
        Assert.Contains("LiteLlm", ex.Message);
    }

    [Fact]
    public void Numeric_provider_value_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => Resolve(Config(("Llm:Provider", "7"))));
    }

    [Fact]
    public void Anthropic_missing_api_key_throws_naming_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Resolve(Config(("Llm:Provider", "Anthropic"))));

        Assert.Contains("Llm:Anthropic:ApiKey", ex.Message);
    }

    [Fact]
    public void LiteLlm_missing_model_throws_naming_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(Config(
            ("Llm:Provider", "LiteLlm"),
            ("Llm:LiteLlm:BaseUrl", "https://proxy/v1/"))));

        Assert.Contains("Llm:LiteLlm:ModelId", ex.Message);
    }

    [Fact]
    public void LiteLlm_missing_base_url_throws_naming_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(Config(
            ("Llm:LiteLlm:ModelId", "m"))));

        Assert.Contains("Llm:LiteLlm:BaseUrl", ex.Message);
    }

    [Fact]
    public void Inactive_provider_config_is_not_validated()
    {
        // Anthropic has no key, but LiteLlm is active — must not throw.
        var client = Resolve(Config(
            ("Llm:Provider", "LiteLlm"),
            ("Llm:LiteLlm:BaseUrl", "https://proxy/v1/"),
            ("Llm:LiteLlm:ModelId", "m")));

        Assert.IsType<LiteLlmChatClient>(client);
    }
}
