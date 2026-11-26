using System.Text.Json;
using System.Text.Json.Serialization;
using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Services;

/// <summary>
/// Raw DTO shape matching exactly what the model returns (snake_case JSON).
/// Kept separate from the domain model (SteelCertificate) so the mapping
/// step is explicit and JSON quirks don't leak into your EF Core entities.
/// </summary>
internal class RawExtractionResponse
{
    [JsonPropertyName("certificates")]
    public List<RawCertificate> Certificates { get; set; } = new();
}

internal class RawCertificate
{
    [JsonPropertyName("heat_number")] public RawField<string>? HeatNumber { get; set; }
    [JsonPropertyName("grade")] public RawField<string>? Grade { get; set; }
    [JsonPropertyName("specification")] public RawField<string>? Specification { get; set; }
    [JsonPropertyName("certifying_lab")] public RawField<string>? CertifyingLab { get; set; }
    [JsonPropertyName("cert_date")] public RawField<string>? CertDate { get; set; }
    [JsonPropertyName("purchase_order_number")] public RawField<string>? PurchaseOrderNumber { get; set; }
    [JsonPropertyName("chemistry")] public RawChemistry? Chemistry { get; set; }
    [JsonPropertyName("mechanical")] public RawMechanical? Mechanical { get; set; }

    // Kept as raw JSON so ONE malformed measurement can be skipped without failing the whole response.
    [JsonPropertyName("measurements")] public JsonElement? Measurements { get; set; }
}

internal class RawChemistry
{
    [JsonPropertyName("carbon_pct")] public RawField<decimal?>? CarbonPct { get; set; }
    [JsonPropertyName("manganese_pct")] public RawField<decimal?>? ManganesePct { get; set; }
    [JsonPropertyName("phosphorus_pct")] public RawField<decimal?>? PhosphorusPct { get; set; }
    [JsonPropertyName("sulfur_pct")] public RawField<decimal?>? SulfurPct { get; set; }
    [JsonPropertyName("silicon_pct")] public RawField<decimal?>? SiliconPct { get; set; }
    [JsonPropertyName("copper_pct")] public RawField<decimal?>? CopperPct { get; set; }
    [JsonPropertyName("nickel_pct")] public RawField<decimal?>? NickelPct { get; set; }
    [JsonPropertyName("chromium_pct")] public RawField<decimal?>? ChromiumPct { get; set; }
    [JsonPropertyName("molybdenum_pct")] public RawField<decimal?>? MolybdenumPct { get; set; }
    [JsonPropertyName("vanadium_pct")] public RawField<decimal?>? VanadiumPct { get; set; }
    [JsonPropertyName("nitrogen_pct")] public RawField<decimal?>? NitrogenPct { get; set; }
    [JsonPropertyName("cobalt_pct")] public RawField<decimal?>? CobaltPct { get; set; }
    [JsonPropertyName("boron_pct")] public RawField<decimal?>? BoronPct { get; set; }
    [JsonPropertyName("aluminum_pct")] public RawField<decimal?>? AluminumPct { get; set; }
    [JsonPropertyName("titanium_pct")] public RawField<decimal?>? TitaniumPct { get; set; }
    [JsonPropertyName("niobium_pct")] public RawField<decimal?>? NiobiumPct { get; set; }
    [JsonPropertyName("tungsten_pct")] public RawField<decimal?>? TungstenPct { get; set; }
    [JsonPropertyName("tin_pct")] public RawField<decimal?>? TinPct { get; set; }
    [JsonPropertyName("lead_pct")] public RawField<decimal?>? LeadPct { get; set; }
    [JsonPropertyName("arsenic_pct")] public RawField<decimal?>? ArsenicPct { get; set; }
    [JsonPropertyName("antimony_pct")] public RawField<decimal?>? AntimonyPct { get; set; }
    [JsonPropertyName("calcium_pct")] public RawField<decimal?>? CalciumPct { get; set; }
    [JsonPropertyName("oxygen_pct")] public RawField<decimal?>? OxygenPct { get; set; }
    [JsonPropertyName("hydrogen_pct")] public RawField<decimal?>? HydrogenPct { get; set; }
    [JsonPropertyName("tantalum_pct")] public RawField<decimal?>? TantalumPct { get; set; }
    [JsonPropertyName("carbon_equivalent")] public RawField<decimal?>? CarbonEquivalent { get; set; }
}

internal class RawMechanical
{
    [JsonPropertyName("yield_strength_psi")] public RawField<decimal?>? YieldStrengthPsi { get; set; }
    [JsonPropertyName("tensile_strength_psi")] public RawField<decimal?>? TensileStrengthPsi { get; set; }
    [JsonPropertyName("elongation_pct")] public RawField<decimal?>? ElongationPct { get; set; }
    [JsonPropertyName("reduction_of_area_pct")] public RawField<string>? ReductionOfAreaPct { get; set; }
    [JsonPropertyName("hardness_value")] public RawField<decimal?>? HardnessValue { get; set; }
    [JsonPropertyName("hardness_scale")] public RawField<string>? HardnessScale { get; set; }
}

internal class RawField<T>
{
    [JsonPropertyName("value")] public T? Value { get; set; }
    // Nullable: the prompt tells the model to null out fields it can't find,
    // and it may null "page" along with "value".
    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("bbox")] public RawBoundingBox? Bbox { get; set; }
}

internal class RawMeasurement
{
    [JsonConverter(typeof(LenientStringConverter))]
    [JsonPropertyName("key")] public string? Key { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    [JsonPropertyName("label")] public string? Label { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    [JsonPropertyName("value")] public string? Value { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    [JsonPropertyName("unit")] public string? Unit { get; set; }

    [JsonConverter(typeof(LenientStringConverter))]
    [JsonPropertyName("condition")] public string? Condition { get; set; }

    [JsonPropertyName("page")] public int? Page { get; set; }
    [JsonPropertyName("bbox")] public RawBoundingBox? Bbox { get; set; }
}

internal class RawBoundingBox
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
}

public interface ICertExtractionService
{
    Task<ExtractionResult> ExtractAsync(
        Stream pdfStream,
        string sourceFileName,
        string? vendorKey,
        CancellationToken ct = default);
}

/// <summary>
/// Orchestrates: PDF -> page images -> vision LLM call (via your existing
/// LiteLLM/Bedrock client) -> parse -> map to domain model.
///
/// This depends on ILlmChatClient as an abstraction over your existing
/// LiteLLM HTTP client so this service has no knowledge of Bedrock auth,
/// routing, or retry policy — that's already solved elsewhere in your stack.
/// </summary>
public class CertExtractionService : ICertExtractionService
{
    // Lenient on numbers: a trace value like "<0.001" or "ND" becomes "not found" for that
    // one field instead of failing the whole extraction (certs print these often).
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LenientDecimalConverter() },
    };

    private readonly IPdfRasterizer _rasterizer;
    private readonly ILlmChatClient _llmClient;
    private readonly List<string> _requiredFields = new()
    {
        nameof(SteelCertificate.HeatNumber),
        nameof(SteelCertificate.Grade),
    };

    public CertExtractionService(IPdfRasterizer rasterizer, ILlmChatClient llmClient)
    {
        _rasterizer = rasterizer;
        _llmClient = llmClient;
    }

    public async Task<ExtractionResult> ExtractAsync(
        Stream pdfStream,
        string sourceFileName,
        string? vendorKey,
        CancellationToken ct = default)
    {
        // 1. Rasterize every page. 150 DPI is a reasonable floor for small
        // table text on a scanned cert; bump to 200 if you see misreads on
        // dense chemistry tables during testing.
        var pageImages = await _rasterizer.RasterizePagesAsync(pdfStream, dpi: 150, ct);

        // 2. Build the prompt and send all pages in a single multimodal call.
        // Sending pages together (rather than per-page) lets the model
        // correlate a heat number on page 1 with chemistry on page 2.
        var systemPrompt = CertExtractionPrompts.SystemPrompt;
        var userPrompt = CertExtractionPrompts.BuildUserPrompt(vendorKey, pageImages.Count);

        var rawJson = await _llmClient.SendMultimodalAsync(
            systemPrompt: systemPrompt,
            userPrompt: userPrompt,
            images: pageImages.Select(p => p.PngBytes).ToList(),
            ct: ct);

        // 3. Parse. Strip markdown fences defensively — models sometimes
        // wrap JSON in ```json even when told not to.
        var cleanJson = StripMarkdownFences(rawJson);
        RawExtractionResponse raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawExtractionResponse>(cleanJson,
                ParseOptions)
                ?? new RawExtractionResponse();
        }
        catch (JsonException ex)
        {
            // Surface the raw response in the exception so a failed
            // extraction is debuggable rather than a silent empty result.
            throw new CertExtractionParseException(
                $"Model response was not valid JSON for '{sourceFileName}'.", rawJson, ex);
        }

        // 4. Map to domain model and flag missing required fields per cert.
        var certificates = raw.Certificates.Select(MapToDomain).ToList();

        return new ExtractionResult
        {
            DocumentId = Guid.NewGuid(),
            SourceFileName = sourceFileName,
            PageCount = pageImages.Count,
            Certificates = certificates,
            ExtractedAtUtc = DateTime.UtcNow,
            ModelUsed = _llmClient.ModelIdentifier,
        };
    }

    private SteelCertificate MapToDomain(RawCertificate raw)
    {
        var cert = new SteelCertificate
        {
            HeatNumber = MapField(raw.HeatNumber),
            Grade = MapField(raw.Grade),
            Specification = MapField(raw.Specification),
            CertifyingLab = MapField(raw.CertifyingLab),
            PurchaseOrderNumber = MapField(raw.PurchaseOrderNumber),
            CertDate = MapDateField(raw.CertDate),
            Chemistry = new ChemistryComposition
            {
                CarbonPct = MapField(raw.Chemistry?.CarbonPct),
                ManganesePct = MapField(raw.Chemistry?.ManganesePct),
                PhosphorusPct = MapField(raw.Chemistry?.PhosphorusPct),
                SulfurPct = MapField(raw.Chemistry?.SulfurPct),
                SiliconPct = MapField(raw.Chemistry?.SiliconPct),
                CopperPct = MapField(raw.Chemistry?.CopperPct),
                NickelPct = MapField(raw.Chemistry?.NickelPct),
                ChromiumPct = MapField(raw.Chemistry?.ChromiumPct),
                MolybdenumPct = MapField(raw.Chemistry?.MolybdenumPct),
                VanadiumPct = MapField(raw.Chemistry?.VanadiumPct),
                NitrogenPct = MapField(raw.Chemistry?.NitrogenPct),
                CobaltPct = MapField(raw.Chemistry?.CobaltPct),
                BoronPct = MapField(raw.Chemistry?.BoronPct),
                AluminumPct = MapField(raw.Chemistry?.AluminumPct),
                TitaniumPct = MapField(raw.Chemistry?.TitaniumPct),
                NiobiumPct = MapField(raw.Chemistry?.NiobiumPct),
                TungstenPct = MapField(raw.Chemistry?.TungstenPct),
                TinPct = MapField(raw.Chemistry?.TinPct),
                LeadPct = MapField(raw.Chemistry?.LeadPct),
                ArsenicPct = MapField(raw.Chemistry?.ArsenicPct),
                AntimonyPct = MapField(raw.Chemistry?.AntimonyPct),
                CalciumPct = MapField(raw.Chemistry?.CalciumPct),
                OxygenPct = MapField(raw.Chemistry?.OxygenPct),
                HydrogenPct = MapField(raw.Chemistry?.HydrogenPct),
                TantalumPct = MapField(raw.Chemistry?.TantalumPct),
                CarbonEquivalent = MapField(raw.Chemistry?.CarbonEquivalent),
            },
            Mechanical = new MechanicalProperties
            {
                YieldStrengthPsi = MapField(raw.Mechanical?.YieldStrengthPsi),
                TensileStrengthPsi = MapField(raw.Mechanical?.TensileStrengthPsi),
                ElongationPct = MapField(raw.Mechanical?.ElongationPct),
                ReductionOfAreaPct = MapField(raw.Mechanical?.ReductionOfAreaPct),
                HardnessValue = MapField(raw.Mechanical?.HardnessValue),
                HardnessScale = MapField(raw.Mechanical?.HardnessScale),
            },
        };

        cert.Measurements = MapMeasurements(raw.Measurements);
        cert.MissingRequiredFields = ComputeMissingRequiredFields(cert);
        return cert;
    }

    private static ExtractedField<T>? MapField<T>(RawField<T>? raw)
    {
        if (raw is null) return null;
        return new ExtractedField<T>
        {
            Value = raw.Value,
            OriginalValue = raw.Value, // what the model said; the review screen never changes this
            PageNumber = raw.Page ?? 0,
            BoundingBox = raw.Bbox is null ? null : new BoundingBox
            {
                X = raw.Bbox.X,
                Y = raw.Bbox.Y,
                W = raw.Bbox.W,
                H = raw.Bbox.H,
            },
            Confirmed = false, // nothing is confirmed until a human says so
        };
    }

    /// <summary>
    /// Maps the model's "measurements" array. Tolerant by design: a missing or non-array
    /// value gives an empty list; a malformed or value-less entry is skipped, never fatal.
    /// One measurement per catalog key and per "other" label (the first listed wins);
    /// keys outside the catalog become "other" with the printed label; at most
    /// <see cref="MeasurementCatalog.MaxOtherPerCertificate"/> "other" entries.
    /// </summary>
    private static List<ExtractedMeasurement> MapMeasurements(JsonElement? element)
    {
        var result = new List<ExtractedMeasurement>();
        if (element is not { ValueKind: JsonValueKind.Array } array) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var otherCount = 0;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            RawMeasurement? raw;
            try
            {
                raw = item.Deserialize<RawMeasurement>(ParseOptions);
            }
            catch (JsonException)
            {
                continue;   // a malformed entry skips only that entry
            }
            if (raw is null) continue;

            var value = new MeasurementValue(raw.Value, raw.Unit, raw.Condition);
            if (value.Value is null) continue;   // nothing printed → nothing to review

            var key = raw.Key?.Trim().ToLowerInvariant() ?? string.Empty;
            var kind = MeasurementCatalog.Find(key);

            string outKey, label, category;
            if (kind is not null)
            {
                (outKey, label, category) = (kind.Key, kind.Label, kind.Category);
            }
            else
            {
                if (otherCount >= MeasurementCatalog.MaxOtherPerCertificate) continue;

                // Name it by the printed label; fall back to the key the model used; else we can't name it.
                var printed = raw.Label?.Trim();
                label = !string.IsNullOrEmpty(printed) ? printed
                      : key.Length > 0 && key != MeasurementCatalog.OtherKey ? key
                      : string.Empty;
                if (label.Length == 0) continue;
                if (label.Length > MeasurementLimits.Label) label = label[..MeasurementLimits.Label];

                (outKey, category) = (MeasurementCatalog.OtherKey, MeasurementCatalog.OtherCategory);
            }

            var identity = outKey == MeasurementCatalog.OtherKey ? $"other:{label}" : outKey;
            if (!seen.Add(identity)) continue;   // one value per property: the first listed wins
            if (outKey == MeasurementCatalog.OtherKey) otherCount++;

            result.Add(new ExtractedMeasurement
            {
                Key = outKey,
                Label = label,
                Category = category,
                Field = new ExtractedField<MeasurementValue>
                {
                    Value = value,
                    OriginalValue = value,   // what the model said; the review screen never changes this
                    PageNumber = raw.Page ?? 0,
                    BoundingBox = raw.Bbox is null ? null : new BoundingBox
                    {
                        X = raw.Bbox.X, Y = raw.Bbox.Y, W = raw.Bbox.W, H = raw.Bbox.H,
                    },
                    Confirmed = false,
                },
            });
        }
        return result;
    }

    private static ExtractedField<DateOnly?>? MapDateField(RawField<string>? raw)
    {
        if (raw is null || string.IsNullOrWhiteSpace(raw.Value)) return null;
        if (!DateOnly.TryParse(raw.Value, out var parsed)) return null;

        return new ExtractedField<DateOnly?>
        {
            Value = parsed,
            OriginalValue = parsed,
            PageNumber = raw.Page ?? 0,
            BoundingBox = raw.Bbox is null ? null : new BoundingBox
            {
                X = raw.Bbox.X, Y = raw.Bbox.Y, W = raw.Bbox.W, H = raw.Bbox.H,
            },
        };
    }

    private List<string> ComputeMissingRequiredFields(SteelCertificate cert)
    {
        var missing = new List<string>();
        if (cert.HeatNumber?.Value is null) missing.Add(nameof(SteelCertificate.HeatNumber));
        if (cert.Grade?.Value is null) missing.Add(nameof(SteelCertificate.Grade));
        return missing;
    }

    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) trimmed = trimmed[..lastFence];
        }
        return trimmed.Trim();
    }
}

public class CertExtractionParseException : Exception
{
    public string RawResponse { get; }
    public CertExtractionParseException(string message, string rawResponse, Exception inner)
        : base(message, inner) => RawResponse = rawResponse;
}

/// <summary>
/// Abstraction over PDF -> page image rasterization. Implement with
/// Docnet.Core or PDFtoImage (both pure managed, no native PDF renderer
/// install headaches) — keep the implementation swappable since this is
/// the piece most likely to change if you hit a library limitation.
/// </summary>
public interface IPdfRasterizer
{
    Task<List<RasterizedPage>> RasterizePagesAsync(Stream pdfStream, int dpi, CancellationToken ct);
}

public record RasterizedPage(int PageNumber, byte[] PngBytes, int WidthPx, int HeightPx);
