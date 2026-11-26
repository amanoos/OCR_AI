namespace SteelCertExtraction.Persistence;

/// <summary>
/// One "other measurement" on a stored certificate (impact energy, grain size,
/// heat treatment, product size, ...): properties whose shape varies too much
/// for typed columns. Holds the FINAL value (after any reviewer edit); what the
/// model originally said lives in the certificate's evidence rows.
///
/// Value, unit and condition are kept exactly as printed on the cert.
/// Removing a measurement in review means no row here (the evidence row remains).
/// </summary>
public class CertificateMeasurement
{
    public int Id { get; set; }
    public int CertificateId { get; set; }
    public StoredCertificate? Certificate { get; set; }

    /// <summary>Catalog key (e.g. "impact_energy") or "other" for anything outside the catalog.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human label: the catalog label, or the label as printed for "other".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Impact, Tensile, HeatTreatment, Product, ...</summary>
    public string Category { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public string? Condition { get; set; }
}
