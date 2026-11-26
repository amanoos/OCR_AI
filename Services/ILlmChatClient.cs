namespace SteelCertExtraction.Services;

/// <summary>
/// Provider-agnostic multimodal chat client. Implementations (LiteLLM proxy,
/// direct Anthropic API) differ only in wire format; extraction code depends
/// on this interface alone.
/// </summary>
public interface ILlmChatClient
{
    string ModelIdentifier { get; }

    Task<string> SendMultimodalAsync(
        string systemPrompt,
        string userPrompt,
        List<byte[]> images,
        CancellationToken ct = default);
}

public class LlmCallException : Exception
{
    public LlmCallException(string message) : base(message) { }
}
