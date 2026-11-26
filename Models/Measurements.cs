namespace SteelCertExtraction.Domain.Models;

/// <summary>
/// The value of one "other measurement", exactly as printed on the certificate:
/// the value text, its unit, and the test condition (e.g. "-40 C, 10x10 mm,
/// longitudinal"). A record, so two values compare by content — which is what
/// makes <see cref="ExtractedField{T}.IsEdited"/> work for measurements too.
/// Blank parts are normalized to null so "" and null never count as an edit.
/// </summary>
public sealed record MeasurementValue
{
    public MeasurementValue(string? value, string? unit = null, string? condition = null)
    {
        Value = Clean(value);
        Unit = Clean(unit);
        Condition = Clean(condition);
    }

    public string? Value { get; init; }
    public string? Unit { get; init; }
    public string? Condition { get; init; }

    /// <summary>"54 J (-40 C, 10x10 mm)" — for display and for the evidence record's original text.</summary>
    public string Display
    {
        get
        {
            var main = string.Join(' ', new[] { Value, Unit }.Where(p => p is not null));
            return Condition is null ? main : $"{main} ({Condition})".TrimStart();
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// One measurement on a certificate: which property it is (catalog key, or
/// "other" with the label as printed), plus the extracted value with its page,
/// bounding box, original and confirmed state.
/// </summary>
public class ExtractedMeasurement
{
    /// <summary>Catalog key (e.g. "impact_energy") or "other".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Catalog label, or the label as printed for "other".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Impact, Tensile, HeatTreatment, Product or Other.</summary>
    public string Category { get; set; } = string.Empty;

    public ExtractedField<MeasurementValue> Field { get; set; } = new();

    /// <summary>Unique per certificate; the name of this measurement's evidence row.</summary>
    public string EvidenceName =>
        Key == MeasurementCatalog.OtherKey ? $"Measurement:other:{Label}" : $"Measurement:{Key}";
}

public sealed record MeasurementKind(string Key, string Label, string Category);

/// <summary>
/// The measurement keys the extraction prompt offers the model. Anything the
/// model returns outside this list is kept as an "other" measurement with the
/// label as printed, so nothing printed is silently dropped.
/// </summary>
public static class MeasurementCatalog
{
    public const string OtherKey = "other";
    public const string OtherCategory = "Other";

    /// <summary>At most this many uncatalogued ("other") measurements per certificate.</summary>
    public const int MaxOtherPerCertificate = 10;

    public static IReadOnlyList<MeasurementKind> Kinds { get; } = new List<MeasurementKind>
    {
        // Impact
        new("impact_energy", "Impact energy", "Impact"),
        new("impact_test_temperature", "Impact test temperature", "Impact"),
        new("impact_specimen_size", "Impact specimen size", "Impact"),
        new("impact_lateral_expansion", "Lateral expansion", "Impact"),
        new("impact_shear_area", "Shear area", "Impact"),

        // Tensile details
        new("yield_offset", "Yield offset", "Tensile"),
        new("elongation_gauge_length", "Elongation gauge length", "Tensile"),
        new("test_direction", "Test direction", "Tensile"),
        new("specimen_type", "Specimen type", "Tensile"),

        // Heat treatment & process
        new("heat_treatment", "Heat treatment", "HeatTreatment"),
        new("anneal_temperature", "Anneal temperature", "HeatTreatment"),
        new("melt_practice", "Melt practice", "HeatTreatment"),
        new("grain_size", "Grain size", "HeatTreatment"),
        new("ferrite_content", "Ferrite content", "HeatTreatment"),
        new("intergranular_corrosion_test", "Intergranular corrosion test", "HeatTreatment"),
        new("reduction_ratio", "Reduction ratio", "HeatTreatment"),

        // Product & traceability
        new("product_form", "Product form", "Product"),
        new("product_size", "Product size", "Product"),
        new("lot_number", "Lot number", "Product"),
        new("quantity", "Quantity", "Product"),
        new("weight", "Weight", "Product"),
        new("mill_name", "Mill name", "Product"),
    };

    private static readonly Dictionary<string, MeasurementKind> ByKey =
        Kinds.ToDictionary(k => k.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Categories { get; } =
        new[] { "Impact", "Tensile", "HeatTreatment", "Product", OtherCategory };

    /// <summary>The catalog entry for a key (case-insensitive), or null when it isn't one.</summary>
    public static MeasurementKind? Find(string? key) =>
        key is not null && ByKey.TryGetValue(key.Trim(), out var kind) ? kind : null;

    public static string CategoryLabel(string category) => category switch
    {
        "Impact" => "Impact",
        "Tensile" => "Tensile details",
        "HeatTreatment" => "Heat treatment & process",
        "Product" => "Product & traceability",
        _ => "Other",
    };
}
