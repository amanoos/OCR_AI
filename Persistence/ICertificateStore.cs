using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Persistence;

/// <summary>
/// Persists a human-confirmed certificate. Expected outcomes (saved, duplicate,
/// invalid) are returned as results rather than thrown; only unexpected
/// failures (e.g. database unavailable) throw.
/// </summary>
public interface ICertificateStore
{
    /// <param name="cert">The certificate as shown on the review screen (with Confirmed flags).</param>
    /// <param name="extraction">The extraction run it came from (document id, file name, model, timestamp).</param>
    /// <param name="storedPdfPath">Where the source PDF was stored / its URL.</param>
    Task<SaveCertificateResult> SaveConfirmedAsync(
        SteelCertificate cert,
        ExtractionResult extraction,
        string storedPdfPath,
        CancellationToken ct = default);
}

/// <summary>The existing record shown to the user when a heat number is already saved.</summary>
public sealed record ExistingCertificateSummary(
    int Id,
    string HeatNumber,
    string Grade,
    string SourceFileName,
    DateTime ConfirmedAtUtc);

public abstract record SaveCertificateResult
{
    private SaveCertificateResult() { }

    /// <summary>Saved as a new record.</summary>
    public sealed record Saved(int Id) : SaveCertificateResult;

    /// <summary>A certificate with the same (normalized) heat number already exists; nothing was written.</summary>
    public sealed record Duplicate(ExistingCertificateSummary Existing) : SaveCertificateResult;

    /// <summary>Required fields missing or unconfirmed; nothing was written.</summary>
    public sealed record Invalid(IReadOnlyList<string> Reasons) : SaveCertificateResult;
}
