using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Persistence;

/// <summary>
/// EF Core implementation. Uses IDbContextFactory because Blazor Server
/// circuits are long-lived and a scoped DbContext would be shared across
/// many user actions.
/// </summary>
public class CertificateStore : ICertificateStore
{
    private readonly IDbContextFactory<CertDbContext> _factory;

    public CertificateStore(IDbContextFactory<CertDbContext> factory) => _factory = factory;

    public async Task<SaveCertificateResult> SaveConfirmedAsync(
        SteelCertificate cert,
        ExtractionResult extraction,
        string storedPdfPath,
        CancellationToken ct = default)
    {
        // Enforced here, not only in the UI: nothing unconfirmed on the
        // required fields may reach the database.
        var reasons = Validate(cert);
        if (reasons.Count > 0)
        {
            return new SaveCertificateResult.Invalid(reasons);
        }

        var stored = CertificateMapper.ToStored(cert, extraction, storedPdfPath, DateTime.UtcNow);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await FindExistingAsync(db, stored.HeatNumber, ct);
        if (existing is not null)
        {
            return new SaveCertificateResult.Duplicate(existing);
        }

        db.Certificates.Add(stored);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another save may have inserted the same heat number between our
            // check and our insert; the unique index rejected us. Provider-
            // agnostic: if the heat number now exists, treat it as a duplicate;
            // otherwise this is a real failure and must surface.
            await using var check = await _factory.CreateDbContextAsync(ct);
            var raced = await FindExistingAsync(check, stored.HeatNumber, ct);
            if (raced is not null)
            {
                return new SaveCertificateResult.Duplicate(raced);
            }
            throw;
        }

        return new SaveCertificateResult.Saved(stored.Id);
    }

    private static List<string> Validate(SteelCertificate cert)
    {
        var reasons = new List<string>();
        CheckRequired(reasons, "Heat Number", cert.HeatNumber?.Value, cert.HeatNumber?.Confirmed ?? false);
        CheckRequired(reasons, "Grade", cert.Grade?.Value, cert.Grade?.Confirmed ?? false);

        // Value rules on the FINAL values — what is about to be stored — whether they
        // came from the model or from a reviewer's edit. Same rules the review screen
        // applies, re-checked here so skipping the screen can't push bad values in,
        // and so over-long text can't turn into a SQL error.
        Text(reasons, nameof(SteelCertificate.HeatNumber), cert.HeatNumber?.Value);
        Text(reasons, nameof(SteelCertificate.Grade), cert.Grade?.Value);
        Text(reasons, nameof(SteelCertificate.Specification), cert.Specification?.Value);
        Text(reasons, nameof(SteelCertificate.CertifyingLab), cert.CertifyingLab?.Value);
        Text(reasons, nameof(SteelCertificate.PurchaseOrderNumber), cert.PurchaseOrderNumber?.Value);
        Add(reasons, ExtractedValueValidator.CheckDate(nameof(SteelCertificate.CertDate), cert.CertDate?.Value));

        var c = cert.Chemistry;
        Number(reasons, nameof(ChemistryComposition.CarbonPct), c.CarbonPct?.Value);
        Number(reasons, nameof(ChemistryComposition.ManganesePct), c.ManganesePct?.Value);
        Number(reasons, nameof(ChemistryComposition.PhosphorusPct), c.PhosphorusPct?.Value);
        Number(reasons, nameof(ChemistryComposition.SulfurPct), c.SulfurPct?.Value);
        Number(reasons, nameof(ChemistryComposition.SiliconPct), c.SiliconPct?.Value);
        Number(reasons, nameof(ChemistryComposition.ChromiumPct), c.ChromiumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.NickelPct), c.NickelPct?.Value);
        Number(reasons, nameof(ChemistryComposition.MolybdenumPct), c.MolybdenumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.CopperPct), c.CopperPct?.Value);
        Number(reasons, nameof(ChemistryComposition.VanadiumPct), c.VanadiumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.NitrogenPct), c.NitrogenPct?.Value);
        Number(reasons, nameof(ChemistryComposition.CobaltPct), c.CobaltPct?.Value);
        Number(reasons, nameof(ChemistryComposition.BoronPct), c.BoronPct?.Value);
        Number(reasons, nameof(ChemistryComposition.AluminumPct), c.AluminumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.TitaniumPct), c.TitaniumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.NiobiumPct), c.NiobiumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.TungstenPct), c.TungstenPct?.Value);
        Number(reasons, nameof(ChemistryComposition.TinPct), c.TinPct?.Value);
        Number(reasons, nameof(ChemistryComposition.LeadPct), c.LeadPct?.Value);
        Number(reasons, nameof(ChemistryComposition.ArsenicPct), c.ArsenicPct?.Value);
        Number(reasons, nameof(ChemistryComposition.AntimonyPct), c.AntimonyPct?.Value);
        Number(reasons, nameof(ChemistryComposition.CalciumPct), c.CalciumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.OxygenPct), c.OxygenPct?.Value);
        Number(reasons, nameof(ChemistryComposition.HydrogenPct), c.HydrogenPct?.Value);
        Number(reasons, nameof(ChemistryComposition.TantalumPct), c.TantalumPct?.Value);
        Number(reasons, nameof(ChemistryComposition.CarbonEquivalent), c.CarbonEquivalent?.Value);

        var m = cert.Mechanical;
        Number(reasons, nameof(MechanicalProperties.YieldStrengthPsi), m.YieldStrengthPsi?.Value);
        Number(reasons, nameof(MechanicalProperties.TensileStrengthPsi), m.TensileStrengthPsi?.Value);
        Number(reasons, nameof(MechanicalProperties.ElongationPct), m.ElongationPct?.Value);
        Text(reasons, nameof(MechanicalProperties.ReductionOfAreaPct), m.ReductionOfAreaPct?.Value);
        Number(reasons, nameof(MechanicalProperties.HardnessValue), m.HardnessValue?.Value);
        Text(reasons, nameof(MechanicalProperties.HardnessScale), m.HardnessScale?.Value);

        CheckMeasurements(reasons, cert);

        return reasons;
    }

    /// <summary>
    /// Only measurements that will actually be saved (they have a value) are checked; a removed
    /// one is simply not saved. Catches an unknown key, a measurement listed twice (the database
    /// allows one per key / "other" label), and over-long or missing parts.
    /// </summary>
    private static void CheckMeasurements(List<string> reasons, SteelCertificate cert)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var measurement in cert.Measurements)
        {
            if (measurement.Field.Value?.Value is null) continue;

            var name = string.IsNullOrWhiteSpace(measurement.Label) ? measurement.Key : measurement.Label;

            if (measurement.Key != MeasurementCatalog.OtherKey && MeasurementCatalog.Find(measurement.Key) is null)
            {
                reasons.Add($"{name}: unknown measurement type '{measurement.Key}'.");
            }
            if (!seen.Add(measurement.EvidenceName))
            {
                reasons.Add($"{name}: appears more than once.");
            }

            reasons.AddRange(MeasurementValidator.Check(measurement));
        }
    }

    // Blank text is handled by the required-field check above (or is fine for an optional field).
    private static void Text(List<string> reasons, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Add(reasons, ExtractedValueValidator.CheckText(key, value));
    }

    private static void Number(List<string> reasons, string key, decimal? value) =>
        Add(reasons, ExtractedValueValidator.CheckNumber(key, value));

    private static void Add(List<string> reasons, string? error)
    {
        if (error is not null) reasons.Add(error);
    }

    private static void CheckRequired(List<string> reasons, string label, string? value, bool confirmed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            reasons.Add($"{label} is missing.");
        }
        else if (!confirmed)
        {
            reasons.Add($"{label} has not been confirmed.");
        }
    }

    private static Task<ExistingCertificateSummary?> FindExistingAsync(
        CertDbContext db, string normalizedHeatNumber, CancellationToken ct) =>
        db.Certificates
            .AsNoTracking()
            .Where(c => c.HeatNumber == normalizedHeatNumber)
            .Select(c => new ExistingCertificateSummary(
                c.Id, c.HeatNumber, c.Grade, c.SourceFileName, c.ConfirmedAtUtc))
            .FirstOrDefaultAsync(ct);
}
