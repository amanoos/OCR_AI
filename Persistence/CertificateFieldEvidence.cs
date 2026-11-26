namespace SteelCertExtraction.Persistence;

/// <summary>
/// Per-field provenance for a stored certificate: what the model extracted,
/// where on the PDF it found it, and whether the reviewer ticked it. One row
/// per extracted field that had a value.
/// </summary>
public class CertificateFieldEvidence
{
    public int Id { get; set; }
    public int CertificateId { get; set; }
    public StoredCertificate? Certificate { get; set; }

    /// <summary>Property name on the extraction model, e.g. "HeatNumber", "CarbonPct".</summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>The value as extracted, as text (e.g. the un-normalized heat number).</summary>
    public string? ExtractedValueText { get; set; }

    /// <summary>1-indexed page; 0 when the model gave none.</summary>
    public int Page { get; set; }

    // Normalized (0-1) bounding box; all NULL when the model gave none.
    public double? BboxX { get; set; }
    public double? BboxY { get; set; }
    public double? BboxW { get; set; }
    public double? BboxH { get; set; }

    public bool Confirmed { get; set; }

    /// <summary>
    /// True when a reviewer changed the value (corrected, added or cleared it).
    /// <see cref="ExtractedValueText"/> is always what the MODEL said (NULL if it
    /// extracted nothing); the final value is in the certificate's own column.
    /// </summary>
    public bool Edited { get; set; }
}
