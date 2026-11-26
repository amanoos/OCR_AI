using System.Diagnostics;

namespace SteelCertExtraction.Services;

/// <summary>
/// `dotnet run --project SteelCertExtraction.csproj -- --check-llm`
///
/// Sends ONE tiny request (a text prompt plus a 1x1 PNG, so image passing
/// through the proxy is exercised too) through the configured ILlmChatClient and
/// prints what happened. Built to shorten debugging when first pointing the app
/// at a new proxy/model (e.g. the office LiteLLM): it shows the exact status and
/// error body, with a hint for the common causes.
///
/// Never prints API keys: any configured key found in output is replaced.
/// No cert data is sent. Returns an exit code: 0 = got a reply, 1 = failed.
/// </summary>
public static class LlmConnectionCheck
{
    // A valid 1x1 PNG; the test suite decodes it to prove that.
    public const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private const int MaxReplyChars = 500;

    public static async Task<int> RunAsync(
        ILlmChatClient client,
        LlmOptions options,
        TextWriter output,
        CancellationToken ct = default)
    {
        var secrets = new[] { options.LiteLlm.ApiKey, options.Anthropic.ApiKey }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();

        string Mask(string text)
        {
            foreach (var secret in secrets)
            {
                text = text.Replace(secret, "***", StringComparison.Ordinal);
            }
            return text;
        }

        var isAnthropic = options.Provider == LlmProvider.Anthropic;
        var baseUrl = isAnthropic ? options.Anthropic.BaseUrl : options.LiteLlm.BaseUrl;
        var hasKey = isAnthropic
            ? !string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)
            : !string.IsNullOrWhiteSpace(options.LiteLlm.ApiKey);

        output.WriteLine("LLM connection check");
        output.WriteLine($"  Provider : {options.Provider}");
        output.WriteLine($"  Base URL : {baseUrl}");
        output.WriteLine($"  Model    : {client.ModelIdentifier}");
        output.WriteLine($"  API key  : {(hasKey ? "configured (not shown)" : "none configured")}");
        output.WriteLine("  Sending  : a short text prompt + a 1x1 PNG image");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var reply = await client.SendMultimodalAsync(
                systemPrompt: "You are a connectivity test. Answer very briefly.",
                userPrompt: "Reply with exactly the word OK.",
                images: new List<byte[]> { Convert.FromBase64String(TinyPngBase64) },
                ct: ct);

            stopwatch.Stop();
            reply = reply.Trim();
            if (reply.Length > MaxReplyChars) reply = reply[..MaxReplyChars] + "…";

            output.WriteLine($"  Result   : OK in {stopwatch.ElapsedMilliseconds} ms");
            output.WriteLine($"  Reply    : {Mask(reply)}");
            return 0;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var message = ex is OperationCanceledException
                ? "The request timed out or was cancelled."
                : $"{ex.GetType().Name}: {ex.Message}";

            output.WriteLine($"  Result   : FAILED after {stopwatch.ElapsedMilliseconds} ms");
            output.WriteLine($"  Error    : {Mask(message)}");

            var hint = HintFor(ex, message, isAnthropic);
            if (hint is not null)
            {
                output.WriteLine($"  Hint     : {hint}");
            }
            return 1;
        }
    }

    private static string? HintFor(Exception ex, string message, bool isAnthropic)
    {
        var configRoot = isAnthropic ? "Llm:Anthropic" : "Llm:LiteLlm";

        if (message.Contains("temperature", StringComparison.OrdinalIgnoreCase))
        {
            return isAnthropic
                ? "The model rejected 'temperature'; the Anthropic client doesn't send it, so check the model id."
                : "The model rejected 'temperature'. The client retries once without it on a 400; if you got a different status, set Llm:LiteLlm:SendTemperature to false.";
        }
        if (message.Contains("NotFound", StringComparison.OrdinalIgnoreCase) || message.Contains("404"))
        {
            return $"Check {configRoot}:BaseUrl (LiteLLM URLs usually end with /v1/) and the model id.";
        }
        if (message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || message.Contains("401") || message.Contains("403"))
        {
            return $"Authentication failed: check {configRoot}:ApiKey (user-secrets / environment variable).";
        }
        if (message.Contains("image", StringComparison.OrdinalIgnoreCase)
            && message.Contains("BadRequest", StringComparison.OrdinalIgnoreCase))
        {
            return "The route/model may not accept image input; extraction needs a vision-capable model.";
        }
        if (message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SSL", StringComparison.OrdinalIgnoreCase))
        {
            return "TLS/certificate problem — a corporate proxy may be intercepting HTTPS; the machine must trust its certificate.";
        }
        if (ex is HttpRequestException || ex is OperationCanceledException)
        {
            return "Could not reach the host: check the Base URL, VPN/network and any proxy settings.";
        }
        return null;
    }
}
