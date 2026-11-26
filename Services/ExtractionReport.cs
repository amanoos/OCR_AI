using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Services;

/// <summary>
/// `dotnet run --project SteelCertExtraction.csproj -- --extract-file &lt;pdf&gt; [--show-raw]`
///
/// Runs the REAL extraction (rasterize → LLM → parse) on one PDF and prints every
/// field the parser produced: value, page, and whether a highlight box came back.
/// Built for tuning the prompt and checking new certs without driving the UI.
/// Nothing is saved, no database or web server is involved, and configured API
/// keys are masked in any output.
/// </summary>
public static class ExtractionReport
{
    private const long MaxFileBytes = 25 * 1024 * 1024;   // same limit as the upload page
    private const int MaxRawCharsOnError = 4000;

    public static async Task<int> RunAsync(
        ILlmChatClient llm,
        IPdfRasterizer rasterizer,
        LlmOptions options,
        string? path,
        bool showRaw,
        TextWriter output,
        CancellationToken ct = default)
    {
        var secrets = new[] { options.LiteLlm.ApiKey, options.Anthropic.ApiKey }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        string Mask(string text)
        {
            foreach (var secret in secrets) text = text.Replace(secret, "***", StringComparison.Ordinal);
            return text;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            output.WriteLine("Usage: dotnet run --project SteelCertExtraction.csproj -- --extract-file <path-to.pdf> [--show-raw]");
            return 2;
        }
        if (!File.Exists(path))
        {
            output.WriteLine($"File not found: {path}");
            return 1;
        }
        if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Only PDF files are supported.");
            return 1;
        }
        if (new FileInfo(path).Length > MaxFileBytes)
        {
            output.WriteLine("File is larger than 25 MB.");
            return 1;
        }

        var recorder = new RecordingClient(llm);
        var service = new CertExtractionService(rasterizer, recorder);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using var stream = File.OpenRead(path);
            var result = await service.ExtractAsync(stream, Path.GetFileName(path), vendorKey: null, ct);
            stopwatch.Stop();

            output.Write(Mask(Format(result, recorder.LastResponse?.Length, stopwatch.ElapsedMilliseconds)));
            if (showRaw && recorder.LastResponse is not null)
            {
                output.WriteLine();
                output.WriteLine("--- raw model response ---");
                output.WriteLine(Mask(recorder.LastResponse));
            }
            return 0;
        }
        catch (CertExtractionParseException ex)
        {
            output.WriteLine($"FAILED: the model's response could not be parsed: {Mask(ex.InnerException?.Message ?? ex.Message)}");
            var raw = Mask(ex.RawResponse);
            output.WriteLine("--- raw model response" + (raw.Length > MaxRawCharsOnError ? $" (first {MaxRawCharsOnError} chars)" : "") + " ---");
            output.WriteLine(raw.Length > MaxRawCharsOnError ? raw[..MaxRawCharsOnError] : raw);
            return 1;
        }
        catch (Exception ex)
        {
            output.WriteLine($"FAILED: {ex.GetType().Name}: {Mask(ex.Message)}");
            return 1;
        }
    }

    /// <summary>Plain-text report of everything the parser produced, in model order.</summary>
    public static string Format(ExtractionResult result, int? rawResponseChars = null, long? elapsedMs = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"File     : {result.SourceFileName}");
        sb.AppendLine($"Pages    : {result.PageCount}");
        sb.AppendLine($"Model    : {result.ModelUsed}");
        if (rawResponseChars is not null) sb.AppendLine($"Response : {rawResponseChars} characters (~{rawResponseChars / 4} tokens)");
        if (elapsedMs is not null) sb.AppendLine($"Elapsed  : {elapsedMs / 1000.0:0.0} s");
        sb.AppendLine($"Certificates found: {result.Certificates.Count}");

        var number = 0;
        foreach (var cert in result.Certificates)
        {
            number++;
            sb.AppendLine();
            sb.AppendLine($"Certificate {number} — heat {cert.HeatNumber?.Value ?? "(not found)"}");

            var total = 0;
            var found = 0;
            foreach (var group in Groups(cert))
            {
                sb.AppendLine($"  [{group.Section}]");
                foreach (var (key, field) in group.Fields)
                {
                    total++;
                    var label = ExtractedValueValidator.RuleFor(key).Label;
                    var (value, page, hasBox) = Describe(field);
                    if (value is not null) found++;

                    sb.Append("    ").Append(label.PadRight(24)).Append(' ')
                      .Append((value ?? "—").PadRight(22));
                    if (value is not null) sb.Append($" p{page} {(hasBox ? "box" : "no box")}");
                    sb.AppendLine();
                }
            }
            sb.AppendLine($"  [Measurements] ({cert.Measurements.Count})");
            if (cert.Measurements.Count == 0)
            {
                sb.AppendLine("    (none)");
            }
            foreach (var m in cert.Measurements)
            {
                var shown = m.Field.Value?.Display ?? "—";
                sb.Append("    ").Append(m.Label.PadRight(30)).Append(' ').Append(shown);
                sb.AppendLine($"   [{m.Category}] p{m.Field.PageNumber} {(m.Field.BoundingBox is null ? "no box" : "box")}");
            }
            sb.AppendLine($"  Fields with a value: {found} of {total}");
        }
        return sb.ToString();
    }

    // --- helpers ---

    private sealed record Group(string Section, List<(string Key, object? Field)> Fields);

    private static IEnumerable<Group> Groups(SteelCertificate cert)
    {
        yield return new Group("Certificate", FieldsOf(typeof(SteelCertificate), cert));
        yield return new Group("Chemistry", FieldsOf(typeof(ChemistryComposition), cert.Chemistry));
        yield return new Group("Mechanical", FieldsOf(typeof(MechanicalProperties), cert.Mechanical));
    }

    private static List<(string Key, object? Field)> FieldsOf(Type type, object instance) =>
        type.GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(ExtractedField<>))
            .Select(p => (p.Name, p.GetValue(instance)))
            .ToList();

    private static (string? Value, int Page, bool HasBox) Describe(object? field)
    {
        if (field is null) return (null, 0, false);

        var type = field.GetType();
        var raw = type.GetProperty("Value")!.GetValue(field);
        var page = (int)type.GetProperty("PageNumber")!.GetValue(field)!;
        var hasBox = type.GetProperty("BoundingBox")!.GetValue(field) is not null;

        string? text = raw switch
        {
            null => null,
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
            string s when string.IsNullOrWhiteSpace(s) => null,
            string s => s.Length > 60 ? s[..60] + "…" : s,
            _ => raw.ToString(),
        };
        return (text, page, hasBox);
    }

    /// <summary>Passes calls through and remembers the last raw response (for size and --show-raw).</summary>
    private sealed class RecordingClient(ILlmChatClient inner) : ILlmChatClient
    {
        public string? LastResponse { get; private set; }

        public string ModelIdentifier => inner.ModelIdentifier;

        public async Task<string> SendMultimodalAsync(
            string systemPrompt, string userPrompt, List<byte[]> images, CancellationToken ct = default)
        {
            var response = await inner.SendMultimodalAsync(systemPrompt, userPrompt, images, ct);
            LastResponse = response;
            return response;
        }
    }
}
