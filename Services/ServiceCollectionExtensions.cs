namespace SteelCertExtraction.Services;

/// <summary>
/// DI registration for the cert-extraction pipeline.
///
/// The active LLM provider is chosen by configuration ("Llm:Provider"):
///   - LiteLlm   : OpenAI-compatible LiteLLM proxy (routes to Bedrock or any
///                 model the proxy exposes). Default.
///   - Anthropic : direct Anthropic Messages API.
/// Only the active provider's HttpClient/client is registered. Invalid or
/// incomplete configuration throws at startup, naming the missing key.
///
/// Legacy keys LiteLlm:BaseUrl / LiteLlm:ApiKey / LiteLlm:ClaudeModelId are
/// still read as a fallback for Llm:LiteLlm:*. Never commit API keys — use
/// user-secrets or environment variables.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCertExtraction(
        this IServiceCollection services, IConfiguration config)
    {
        var options = ResolveOptions(config, out var usedLegacyLiteLlmKeys);
        Validate(options);

        // Multimodal calls with several page images can be slow.
        var timeout = TimeSpan.FromMinutes(2);

        switch (options.Provider)
        {
            case LlmProvider.LiteLlm:
            {
                var o = options.LiteLlm;
                services.AddHttpClient("llm", client =>
                {
                    client.BaseAddress = ToBaseAddress(o.BaseUrl!);
                    if (!string.IsNullOrWhiteSpace(o.ApiKey))
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", o.ApiKey);
                    }
                    client.Timeout = timeout;
                });
                services.AddScoped<ILlmChatClient>(sp => new LiteLlmChatClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"), o.ModelId!, o.SendTemperature));
                break;
            }
            case LlmProvider.Anthropic:
            {
                var o = options.Anthropic;
                services.AddHttpClient("llm", client =>
                {
                    client.BaseAddress = ToBaseAddress(o.BaseUrl);
                    client.Timeout = timeout;
                });
                services.AddScoped<ILlmChatClient>(sp => new AnthropicChatClient(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"),
                    o.ModelId, o.ApiKey!, o.ApiVersion));
                break;
            }
        }

        services.AddSingleton(options);
        services.AddHostedService(sp => new LlmConfigLogger(
            sp.GetRequiredService<ILogger<LlmConfigLogger>>(), options, usedLegacyLiteLlmKeys));

        services.AddScoped<IPdfRasterizer, PdfToImageRasterizer>();
        services.AddScoped<ICertExtractionService, CertExtractionService>();

        return services;
    }

    /// <summary>Builds options from "Llm:*", falling back to legacy "LiteLlm:*" keys.</summary>
    private static LlmOptions ResolveOptions(IConfiguration config, out bool usedLegacy)
    {
        var options = new LlmOptions();

        var providerText = config[$"{LlmOptions.SectionName}:Provider"];
        if (!string.IsNullOrWhiteSpace(providerText))
        {
            if (!Enum.TryParse<LlmProvider>(providerText, ignoreCase: true, out var provider)
                || !Enum.IsDefined(provider))
            {
                throw new InvalidOperationException(
                    $"Llm:Provider '{providerText}' is not valid. Use one of: " +
                    $"{string.Join(", ", Enum.GetNames<LlmProvider>())}.");
            }
            options.Provider = provider;
        }

        var section = config.GetSection(LlmOptions.SectionName);
        section.GetSection("LiteLlm").Bind(options.LiteLlm);
        section.GetSection("Anthropic").Bind(options.Anthropic);

        var legacyUsed = false;
        string? Fallback(string? current, string legacyKey)
        {
            if (!string.IsNullOrWhiteSpace(current)) return current;
            var legacy = config[legacyKey];
            if (string.IsNullOrWhiteSpace(legacy)) return current;
            legacyUsed = true;
            return legacy;
        }

        options.LiteLlm.BaseUrl = Fallback(options.LiteLlm.BaseUrl, "LiteLlm:BaseUrl");
        options.LiteLlm.ApiKey = Fallback(options.LiteLlm.ApiKey, "LiteLlm:ApiKey");
        options.LiteLlm.ModelId = Fallback(options.LiteLlm.ModelId, "LiteLlm:ClaudeModelId");

        usedLegacy = legacyUsed;
        return options;
    }

    private static void Validate(LlmOptions options)
    {
        switch (options.Provider)
        {
            case LlmProvider.LiteLlm:
                Require(options.LiteLlm.BaseUrl, "Llm:LiteLlm:BaseUrl", options.Provider);
                Require(options.LiteLlm.ModelId, "Llm:LiteLlm:ModelId", options.Provider);
                // ApiKey is optional: some proxies don't require auth.
                break;
            case LlmProvider.Anthropic:
                Require(options.Anthropic.BaseUrl, "Llm:Anthropic:BaseUrl", options.Provider);
                Require(options.Anthropic.ApiKey, "Llm:Anthropic:ApiKey", options.Provider);
                Require(options.Anthropic.ModelId, "Llm:Anthropic:ModelId", options.Provider);
                break;
        }
    }

    private static void Require(string? value, string key, LlmProvider provider)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{key} is not configured (required when Llm:Provider is {provider}). " +
                "Set it in appsettings.json, user-secrets, or an environment variable.");
        }
    }

    // HttpClient resolves relative request paths against BaseAddress only if
    // it ends with '/', so normalize.
    private static Uri ToBaseAddress(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private sealed class LlmConfigLogger : IHostedService
    {
        private readonly ILogger<LlmConfigLogger> _logger;
        private readonly LlmOptions _options;
        private readonly bool _usedLegacy;

        public LlmConfigLogger(ILogger<LlmConfigLogger> logger, LlmOptions options, bool usedLegacy)
        {
            _logger = logger;
            _options = options;
            _usedLegacy = usedLegacy;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var model = _options.Provider == LlmProvider.Anthropic
                ? _options.Anthropic.ModelId
                : _options.LiteLlm.ModelId;
            _logger.LogInformation("LLM provider: {Provider}, model: {Model}", _options.Provider, model);
            if (_usedLegacy)
            {
                _logger.LogWarning(
                    "Using legacy LiteLlm:* configuration keys as a fallback. Migrate to Llm:LiteLlm:*.");
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
