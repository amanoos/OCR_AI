# Spec: Multi-Provider LLM Access (Direct Anthropic + LiteLLM/Bedrock) — V1

## Objective
The cert-extraction pipeline must work against either:
1. **LiteLLM proxy** (OpenAI-compatible `chat/completions`; routes to AWS Bedrock or any model the proxy exposes) — the work/corporate path, already implemented.
2. **Direct Anthropic API** (native Messages API) — for personal/dev use without a proxy.

One provider is active per deployment, selected by configuration. No change to extraction, review UI, or domain models.

User: the developer/operator of the cert-extraction app (switches environments via config, not code).

## Tech Stack
.NET 10, Blazor Server, `HttpClient` via `IHttpClientFactory`, System.Text.Json, xUnit for tests (new test project; no new runtime packages).

## Commands
```
Build:  dotnet build SteelCertExtraction.csproj
Run:    dotnet run --project SteelCertExtraction.csproj
Test:   dotnet test tests/SteelCertExtraction.Tests
Secret: dotnet user-secrets set "Llm:Anthropic:ApiKey" "<key>" --project SteelCertExtraction.csproj
```

## Project Structure
```
Services/ILlmChatClient.cs            → interface (moved out of CertExtractionService.cs)
Services/LiteLlmChatClient.cs         → existing, OpenAI-compatible (unchanged behavior)
Services/AnthropicChatClient.cs       → NEW, native Messages API
Services/LlmOptions.cs                → NEW, bound config (Provider + per-provider sections)
Services/ServiceCollectionExtensions.cs → selects provider at registration
tests/SteelCertExtraction.Tests/      → NEW, fake-HttpMessageHandler tests
```

## Configuration
```json
"Llm": {
  "Provider": "LiteLlm",            // "LiteLlm" | "Anthropic"
  "LiteLlm":   { "BaseUrl": "https://proxy/v1/", "ApiKey": "", "ModelId": "..." },
  "Anthropic": { "BaseUrl": "https://api.anthropic.com/", "ApiKey": "", "ModelId": "claude-sonnet-5-5", "ApiVersion": "2023-06-01" }
}
```
- Existing `LiteLlm:*` keys migrate to `Llm:LiteLlm:*` (old keys read as a fallback for one version, so current setups keep working).
- Unknown/missing `Provider` or a missing ApiKey/ModelId for the active provider fails fast at startup with a clear message naming the missing key.
- LiteLLM `ModelId` is just whatever the proxy exposes (Bedrock or Anthropic-backed); the client is provider-agnostic.

## Behavior Contract (both clients)
`ILlmChatClient.SendMultimodalAsync(systemPrompt, userPrompt, images, ct)` returns the model's text; `ModelIdentifier` returns the configured model id. Non-success HTTP → `LlmCallException` including status and body. Empty content → `LlmCallException`.

**AnthropicChatClient wire format:** `POST {BaseUrl}v1/messages` (relative path, no leading slash); headers `x-api-key`, `anthropic-version`; body `{ model, max_tokens, temperature: 0, system, messages:[{role:"user", content:[{type:"text"}, {type:"image", source:{type:"base64", media_type:"image/png", data}}...]}] }`; response text = concatenation of `content[]` blocks where `type == "text"`.

## Code Style
Match existing files: file-scoped namespaces, nullable enabled, small private DTOs with `[JsonPropertyName]`, XML-doc summary on public types.

```csharp
public async Task<string> SendMultimodalAsync(string systemPrompt, string userPrompt,
    List<byte[]> images, CancellationToken ct = default)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { /* ... */ };
    using var response = await _httpClient.SendAsync(request, ct);
    if (!response.IsSuccessStatusCode)
        throw new LlmCallException($"Anthropic call failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
    // ...
}
```

## Testing Strategy
- New xUnit project `tests/SteelCertExtraction.Tests`.
- Each client tested with a fake `HttpMessageHandler`: asserts request URL (incl. base-path preservation — regression for the `/v1/` bug), headers, JSON body shape (images as correct content parts), response parsing, and error/empty-content paths.
- DI tests: each `Provider` value resolves the right `ILlmChatClient`; invalid/missing config throws at startup.
- Live calls against real APIs are a **manual** smoke test (one real cert page per provider); not automated.

## Boundaries
- Always: use relative request paths; keep `temperature 0`; run `dotnet build` + `dotnet test` before declaring done; keep extraction/UI code provider-agnostic.
- Ask first: adding NuGet packages (e.g. the Anthropic SDK), changing the `ILlmChatClient` signature, adding streaming or tool-use.
- Never: commit API keys (use user-secrets/env vars); let provider-specific types leak outside `Services/`; send cert contents to a provider other than the configured one.

## Success Criteria
1. With `Llm:Provider = LiteLlm`, behavior is identical to today.
2. With `Llm:Provider = Anthropic` and a valid key, a cert PDF extracts end to end with no other code/config change.
3. Switching providers requires only config changes.
4. Startup fails with a specific message for a bad provider name or missing key/model.
5. All new unit tests pass; solution builds with 0 warnings.
6. Manual smoke test of one real cert page succeeds on each provider.

## Out of Scope (Deferred)
- Per-request provider/model selection in the UI — deferred; config switch is enough now.
- Automatic fallback between providers — deferred; adds failure-mode complexity.
- Streaming responses, retries/backoff policy, token/cost tracking — not needed at a few PDFs/week.
- Live-API integration tests in CI.
- Direct AWS Bedrock access without LiteLLM.

## Open Questions
1. Default Anthropic model id: `claude-sonnet-5-5`, or a different tier (cost vs. accuracy on dense chemistry tables)?
2. Should the legacy `LiteLlm:*` config fallback be kept, or is a clean break acceptable?
