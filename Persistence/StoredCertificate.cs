namespace SteelCertExtraction.Persistence;

/// <summary>
/// A human-confirmed certificate as stored in SQL Server. Separate from the
/// extraction/UI models (SteelCertificate, ExtractedField&lt;T&gt;) on purpose:
/// the schema can evolve without touching extraction or the review UI.
///
/// Every numeric column is nullable — NULL means "not found on the cert",
/// never 0.
/// </summary>
public class StoredCertificate
{
    public int Id { get; set; }

    /// <summary>Normalized (trimmed, upper-cased) heat number; unique. The as-extracted text lives in the evidence rows.</summary>
    public string HeatNumber { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string? Specification { get; set; }
    public string? CertifyingLab { get; set; }
    public DateOnly? CertDate { get; set; }
    public string? PurchaseOrderNumber { get; set; }

    // Chemistry (%)
    public decimal? CarbonPct { get; set; }
    public decimal? ManganesePct { get; set; }
    public decimal? PhosphorusPct { get; set; }
    public decimal? SulfurPct { get; set; }
    public decimal? SiliconPct { get; set; }
    public decimal? CopperPct { get; set; }
    public decimal? NickelPct { get; set; }
    public decimal? ChromiumPct { get; set; }
    public decimal? MolybdenumPct { get; set; }
    public decimal? VanadiumPct { get; set; }

    // More elements (weight %). NULL = not on the cert, never 0.
    public decimal? NitrogenPct { get; set; }
    public decimal? CobaltPct { get; set; }
    public decimal? BoronPct { get; set; }
    public decimal? AluminumPct { get; set; }
    public decimal? TitaniumPct { get; set; }
    public decimal? NiobiumPct { get; set; }
    public decimal? TungstenPct { get; set; }
    public decimal? TinPct { get; set; }
    public decimal? LeadPct { get; set; }
    public decimal? ArsenicPct { get; set; }
    public decimal? AntimonyPct { get; set; }
    public decimal? CalciumPct { get; set; }
    public decimal? OxygenPct { get; set; }
    public decimal? HydrogenPct { get; set; }
    public decimal? TantalumPct { get; set; }

    /// <summary>Carbon equivalent as printed on the cert (a computed value, not an element).</summary>
    public decimal? CarbonEquivalent { get; set; }

    // Mechanical (normalized to psi at extraction time)
    public decimal? YieldStrengthPsi { get; set; }
    public decimal? TensileStrengthPsi { get; set; }
    public decimal? ElongationPct { get; set; }
    public string? ReductionOfAreaPct { get; set; }

    /// <summary>Hardness as printed (one value per cert) and the scale it is on: HB, HRC, HV, ...</summary>
    public decimal? HardnessValue { get; set; }
    public string? HardnessScale { get; set; }

    // Provenance of the document and the extraction run
    public string SourceFileName { get; set; } = string.Empty;
    public string StoredPdfPath { get; set; } = string.Empty;
    public Guid ExtractionDocumentId { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public DateTime ExtractedAtUtc { get; set; }
    public DateTime ConfirmedAtUtc { get; set; }

    public List<CertificateFieldEvidence> Evidence { get; set; } = new();

    /// <summary>Flexible "other measurements" (impact, heat treatment, product info, ...), final values.</summary>
    public List<CertificateMeasurement> Measurements { get; set; } = new();
}
