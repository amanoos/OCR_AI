namespace SteelCertExtraction.Services;

public enum LlmProvider
{
    LiteLlm,
    Anthropic,
}

/// <summary>
/// Bound from the "Llm" configuration section. Exactly one provider is
/// active per deployment, chosen by <see cref="Provider"/>.
/// </summary>
public class LlmOptions
{
    public const string SectionName = "Llm";

    public LlmProvider Provider { get; set; } = LlmProvider.LiteLlm;
    public LiteLlmOptions LiteLlm { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
}

/// <summary>OpenAI-compatible LiteLLM proxy (routes to Bedrock or any model it exposes).</summary>
public class LiteLlmOptions
{
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelId { get; set; }

    /// <summary>
    /// Send "temperature": 0 for deterministic extraction. Set false if the
    /// proxy/model rejects it (newer Claude models do). Independently, the
    /// client retries once without it if it gets a 400 mentioning "temperature".
    /// </summary>
    public bool SendTemperature { get; set; } = true;
}

/// <summary>Direct Anthropic Messages API.</summary>
public class AnthropicOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
    public string? ApiKey { get; set; }
    public string ModelId { get; set; } = "claude-sonnet-5-5";
    public string ApiVersion { get; set; } = "2023-06-01";
}
