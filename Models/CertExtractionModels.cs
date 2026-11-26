namespace SteelCertExtraction.Domain.Models;

/// <summary>
/// Normalized bounding box, expressed as fractions (0-1) of page width/height.
/// Resolution-independent so it works regardless of the DPI used to rasterize
/// the page or the size of the PDF.js canvas the user is viewing.
/// </summary>
public class BoundingBox
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
}

/// <summary>
/// A single extracted field, paired with where on the source PDF it was found.
/// PageNumber is 1-indexed to match how PDF.js and humans both think about pages.
/// </summary>
public class ExtractedField<T>
{
    public T? Value { get; set; }

    /// <summary>
    /// What the MODEL extracted (null if it extracted nothing). Set once when the
    /// model response is mapped and never changed by the review screen, so an
    /// edited value can always be compared with — and reverted to — the original.
    /// </summary>
    public T? OriginalValue { get; set; }

    /// <summary>
    /// True when a reviewer changed the value (corrected, added or cleared it).
    /// Derived, so it can never drift: setting Value back to the original makes
    /// this false again. Strings compare trimmed, and null equals blank.
    /// </summary>
    public bool IsEdited => !SameValue(Value, OriginalValue);

    private static bool SameValue(T? a, T? b)
    {
        if (typeof(T) == typeof(string))
        {
            var x = ((object?)a as string)?.Trim() ?? string.Empty;
            var y = ((object?)b as string)?.Trim() ?? string.Empty;
            return x == y;
        }
        return EqualityComparer<T?>.Default.Equals(a, b);
    }

    public int PageNumber { get; set; }
    public BoundingBox? BoundingBox { get; set; }

    /// <summary>
    /// True once a user has explicitly reviewed/confirmed this field in the UI.
    /// Drives the "confirm before save" workflow — nothing is trusted by default.
    /// </summary>
    public bool Confirmed { get; set; }
}

public class ChemistryComposition
{
    // decimal? (not decimal): `T? Value` on an unconstrained generic is NOT
    // nullable for value types, so "not found" must be expressed in T itself.
    public ExtractedField<decimal?>? CarbonPct { get; set; }
    public ExtractedField<decimal?>? ManganesePct { get; set; }
    public ExtractedField<decimal?>? PhosphorusPct { get; set; }
    public ExtractedField<decimal?>? SulfurPct { get; set; }
    public ExtractedField<decimal?>? SiliconPct { get; set; }
    public ExtractedField<decimal?>? CopperPct { get; set; }
    public ExtractedField<decimal?>? NickelPct { get; set; }
    public ExtractedField<decimal?>? ChromiumPct { get; set; }
    public ExtractedField<decimal?>? MolybdenumPct { get; set; }
    public ExtractedField<decimal?>? VanadiumPct { get; set; }

    // More elements — always weight %. If the cert prints ppm the model converts (÷ 10,000).
    public ExtractedField<decimal?>? NitrogenPct { get; set; }
    public ExtractedField<decimal?>? CobaltPct { get; set; }
    public ExtractedField<decimal?>? BoronPct { get; set; }
    public ExtractedField<decimal?>? AluminumPct { get; set; }
    public ExtractedField<decimal?>? TitaniumPct { get; set; }
    public ExtractedField<decimal?>? NiobiumPct { get; set; }
    public ExtractedField<decimal?>? TungstenPct { get; set; }
    public ExtractedField<decimal?>? TinPct { get; set; }
    public ExtractedField<decimal?>? LeadPct { get; set; }
    public ExtractedField<decimal?>? ArsenicPct { get; set; }
    public ExtractedField<decimal?>? AntimonyPct { get; set; }
    public ExtractedField<decimal?>? CalciumPct { get; set; }
    public ExtractedField<decimal?>? OxygenPct { get; set; }
    public ExtractedField<decimal?>? HydrogenPct { get; set; }
    public ExtractedField<decimal?>? TantalumPct { get; set; }

    /// <summary>Carbon equivalent as printed on the cert (a computed value, not an element).</summary>
    public ExtractedField<decimal?>? CarbonEquivalent { get; set; }
}

public class MechanicalProperties
{
    /// <summary>Always normalized to psi at extraction time, regardless of source units (ksi/MPa).</summary>
    public ExtractedField<decimal?>? YieldStrengthPsi { get; set; }
    public ExtractedField<decimal?>? TensileStrengthPsi { get; set; }
    public ExtractedField<decimal?>? ElongationPct { get; set; }
    public ExtractedField<string>? ReductionOfAreaPct { get; set; }

    /// <summary>Hardness as printed (one value per cert; the first listed if several) and its scale: HB, HRC, HV, ...</summary>
    public ExtractedField<decimal?>? HardnessValue { get; set; }
    public ExtractedField<string>? HardnessScale { get; set; }
}

/// <summary>
/// One full certificate extraction. A single PDF may contain multiple
/// certs (one per heat number) — hence ExtractionResult wraps a list of these.
/// </summary>
public class SteelCertificate
{
    public ExtractedField<string>? HeatNumber { get; set; }
    public ExtractedField<string>? Grade { get; set; }            // e.g. "A572-50"
    public ExtractedField<string>? Specification { get; set; }    // e.g. "ASTM A572"
    public ExtractedField<string>? CertifyingLab { get; set; }
    // DateOnly? (not DateOnly): `T? Value` is not nullable for value types, so a
    // cleared/missing date must be expressed in T itself (same as the decimals).
    public ExtractedField<DateOnly?>? CertDate { get; set; }
    public ExtractedField<string>? PurchaseOrderNumber { get; set; }
    public ChemistryComposition Chemistry { get; set; } = new();
    public MechanicalProperties Mechanical { get; set; } = new();

    /// <summary>
    /// Fields the model could not find / left null. Surfaced in the UI
    /// as "needs attention" rather than silently treated as confirmed.
    /// </summary>
    public List<string> MissingRequiredFields { get; set; } = new();

    /// <summary>
    /// "Other measurements" (impact, heat treatment, product info, ...): properties whose
    /// shape varies too much for fixed fields. One per catalog key; "other" ones unique by label.
    /// </summary>
    public List<ExtractedMeasurement> Measurements { get; set; } = new();
}

public class ExtractionResult
{
    public Guid DocumentId { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public List<SteelCertificate> Certificates { get; set; } = new();
    public DateTime ExtractedAtUtc { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
}
