using MudBlazor.Services;
using SteelCertExtraction.Components;
using SteelCertExtraction.Persistence;
using SteelCertExtraction.Services;

var builder = WebApplication.CreateBuilder(args);

// `dotnet run --project SteelCertExtraction.csproj -- --check-llm`
// Sends one tiny request through the configured LLM provider, prints the result
// and exits. Uses only the LLM registrations — no database, no web server.
if (args.Contains("--check-llm"))
{
    var checkServices = new ServiceCollection();
    checkServices.AddLogging();
    checkServices.AddCertExtraction(builder.Configuration);
    // ASP0000 warns about a duplicate singleton container *inside a running web host*.
    // This is a standalone, one-shot container for the CLI check — no host exists.
#pragma warning disable ASP0000
    await using var checkProvider = checkServices.BuildServiceProvider();
#pragma warning restore ASP0000
    await using var checkScope = checkProvider.CreateAsyncScope();
    using var checkCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

    Environment.ExitCode = await LlmConnectionCheck.RunAsync(
        checkScope.ServiceProvider.GetRequiredService<ILlmChatClient>(),
        checkProvider.GetRequiredService<LlmOptions>(),
        Console.Out,
        checkCts.Token);
    return;
}

// `dotnet run --project SteelCertExtraction.csproj -- --extract-file <pdf> [--show-raw]`
// Runs the real extraction on one PDF and prints what was parsed — for checking the
// prompt/parsing on a new cert without the UI. No database, no web server, nothing saved.
if (args.Contains("--extract-file"))
{
    var fileIndex = Array.IndexOf(args, "--extract-file");
    var pdfPath = fileIndex + 1 < args.Length ? args[fileIndex + 1] : null;

    var extractServices = new ServiceCollection();
    extractServices.AddLogging();
    extractServices.AddCertExtraction(builder.Configuration);
#pragma warning disable ASP0000
    await using var extractProvider = extractServices.BuildServiceProvider();
#pragma warning restore ASP0000
    await using var extractScope = extractProvider.CreateAsyncScope();
    using var extractCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

    Environment.ExitCode = await ExtractionReport.RunAsync(
        extractScope.ServiceProvider.GetRequiredService<ILlmChatClient>(),
        extractScope.ServiceProvider.GetRequiredService<IPdfRasterizer>(),
        extractProvider.GetRequiredService<LlmOptions>(),
        pdfPath,
        showRaw: args.Contains("--show-raw"),
        Console.Out,
        extractCts.Token);
    return;
}

// Razor Components with global Interactive Server rendering — the cert
// review/upload UI relies on JS interop, event handlers, and SignalR-backed
// interactivity, so server interactivity is required.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Multi-MB cert PDFs are streamed up over the SignalR circuit via
    // IBrowserFile.OpenReadStream. The default 32KB receive cap trips on
    // anything but a tiny file, so raise it to match the 25MB upload limit
    // enforced in CertUploadPage.
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 32 * 1024 * 1024);

builder.Services.AddMudServices();

// Registers the LiteLLM chat client, PDF rasterizer, and extraction service.
builder.Services.AddCertExtraction(builder.Configuration);

// SQL Server persistence for confirmed certificates. Requires
// ConnectionStrings:CertDb (user-secrets / env var) — fails fast if missing.
builder.Services.AddCertPersistence(builder.Configuration);

var app = builder.Build();

// Development only: create/upgrade the CertExtraction database from the
// committed migrations. Elsewhere, run `dotnet ef database update` deliberately.
if (app.Environment.IsDevelopment())
{
    await app.MigrateCertDatabaseAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

// MapStaticAssets serves only build-time fingerprinted assets. The uploaded
// cert PDFs are written to wwwroot/uploaded-certs at RUNTIME, so the PDF.js
// viewer needs classic static-file middleware to fetch them — the two
// coexist. Without UseStaticFiles the viewer would 404 every uploaded PDF.
app.MapStaticAssets();
app.UseStaticFiles();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
