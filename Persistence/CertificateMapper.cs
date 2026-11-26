using System.Globalization;
using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Persistence;

/// <summary>
/// Explicit mapping from the extraction/UI model to the persistence entities.
///
/// This does NOT validate: the store enforces "Heat Number + Grade present and
/// confirmed" before calling it. A missing numeric value maps to NULL (never
/// 0), and a field with no value produces no evidence row.
/// </summary>
public static class CertificateMapper
{
    /// <summary>Canonical form used for the unique heat-number key: trimmed, upper-cased.</summary>
    public static string NormalizeHeatNumber(string heatNumber) =>
        heatNumber.Trim().ToUpperInvariant();

    public static StoredCertificate ToStored(
        SteelCertificate cert,
        ExtractionResult extraction,
        string storedPdfPath,
        DateTime confirmedAtUtc)
    {
        var stored = new StoredCertificate
        {
            HeatNumber = NormalizeHeatNumber(cert.HeatNumber?.Value ?? string.Empty),
            Grade = cert.Grade?.Value?.Trim() ?? string.Empty,
            Specification = cert.Specification?.Value,
            CertifyingLab = cert.CertifyingLab?.Value,
            CertDate = cert.CertDate?.Value,
            PurchaseOrderNumber = cert.PurchaseOrderNumber?.Value,

            CarbonPct = cert.Chemistry.CarbonPct?.Value,
            ManganesePct = cert.Chemistry.ManganesePct?.Value,
            PhosphorusPct = cert.Chemistry.PhosphorusPct?.Value,
            SulfurPct = cert.Chemistry.SulfurPct?.Value,
            SiliconPct = cert.Chemistry.SiliconPct?.Value,
            CopperPct = cert.Chemistry.CopperPct?.Value,
            NickelPct = cert.Chemistry.NickelPct?.Value,
            ChromiumPct = cert.Chemistry.ChromiumPct?.Value,
            MolybdenumPct = cert.Chemistry.MolybdenumPct?.Value,
            VanadiumPct = cert.Chemistry.VanadiumPct?.Value,
            NitrogenPct = cert.Chemistry.NitrogenPct?.Value,
            CobaltPct = cert.Chemistry.CobaltPct?.Value,
            BoronPct = cert.Chemistry.BoronPct?.Value,
            AluminumPct = cert.Chemistry.AluminumPct?.Value,
            TitaniumPct = cert.Chemistry.TitaniumPct?.Value,
            NiobiumPct = cert.Chemistry.NiobiumPct?.Value,
            TungstenPct = cert.Chemistry.TungstenPct?.Value,
            TinPct = cert.Chemistry.TinPct?.Value,
            LeadPct = cert.Chemistry.LeadPct?.Value,
            ArsenicPct = cert.Chemistry.ArsenicPct?.Value,
            AntimonyPct = cert.Chemistry.AntimonyPct?.Value,
            CalciumPct = cert.Chemistry.CalciumPct?.Value,
            OxygenPct = cert.Chemistry.OxygenPct?.Value,
            HydrogenPct = cert.Chemistry.HydrogenPct?.Value,
            TantalumPct = cert.Chemistry.TantalumPct?.Value,
            CarbonEquivalent = cert.Chemistry.CarbonEquivalent?.Value,

            YieldStrengthPsi = cert.Mechanical.YieldStrengthPsi?.Value,
            TensileStrengthPsi = cert.Mechanical.TensileStrengthPsi?.Value,
            ElongationPct = cert.Mechanical.ElongationPct?.Value,
            ReductionOfAreaPct = cert.Mechanical.ReductionOfAreaPct?.Value,
            HardnessValue = cert.Mechanical.HardnessValue?.Value,
            HardnessScale = cert.Mechanical.HardnessScale?.Value,

            SourceFileName = extraction.SourceFileName,
            StoredPdfPath = storedPdfPath,
            ExtractionDocumentId = extraction.DocumentId,
            ModelUsed = extraction.ModelUsed,
            ExtractedAtUtc = extraction.ExtractedAtUtc,
            ConfirmedAtUtc = confirmedAtUtc,
        };

        AddEvidence(stored, nameof(SteelCertificate.HeatNumber), cert.HeatNumber, v => v);
        AddEvidence(stored, nameof(SteelCertificate.Grade), cert.Grade, v => v);
        AddEvidence(stored, nameof(SteelCertificate.Specification), cert.Specification, v => v);
        AddEvidence(stored, nameof(SteelCertificate.CertifyingLab), cert.CertifyingLab, v => v);
        AddEvidence(stored, nameof(SteelCertificate.CertDate), cert.CertDate,
            v => v!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        AddEvidence(stored, nameof(SteelCertificate.PurchaseOrderNumber), cert.PurchaseOrderNumber, v => v);

        var c = cert.Chemistry;
        AddDecimalEvidence(stored, nameof(ChemistryComposition.CarbonPct), c.CarbonPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.ManganesePct), c.ManganesePct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.PhosphorusPct), c.PhosphorusPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.SulfurPct), c.SulfurPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.SiliconPct), c.SiliconPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.CopperPct), c.CopperPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.NickelPct), c.NickelPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.ChromiumPct), c.ChromiumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.MolybdenumPct), c.MolybdenumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.VanadiumPct), c.VanadiumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.NitrogenPct), c.NitrogenPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.CobaltPct), c.CobaltPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.BoronPct), c.BoronPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.AluminumPct), c.AluminumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.TitaniumPct), c.TitaniumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.NiobiumPct), c.NiobiumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.TungstenPct), c.TungstenPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.TinPct), c.TinPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.LeadPct), c.LeadPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.ArsenicPct), c.ArsenicPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.AntimonyPct), c.AntimonyPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.CalciumPct), c.CalciumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.OxygenPct), c.OxygenPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.HydrogenPct), c.HydrogenPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.TantalumPct), c.TantalumPct);
        AddDecimalEvidence(stored, nameof(ChemistryComposition.CarbonEquivalent), c.CarbonEquivalent);

        var m = cert.Mechanical;
        AddDecimalEvidence(stored, nameof(MechanicalProperties.YieldStrengthPsi), m.YieldStrengthPsi);
        AddDecimalEvidence(stored, nameof(MechanicalProperties.TensileStrengthPsi), m.TensileStrengthPsi);
        AddDecimalEvidence(stored, nameof(MechanicalProperties.ElongationPct), m.ElongationPct);
        AddEvidence(stored, nameof(MechanicalProperties.ReductionOfAreaPct), m.ReductionOfAreaPct, v => v);
        AddDecimalEvidence(stored, nameof(MechanicalProperties.HardnessValue), m.HardnessValue);
        AddEvidence(stored, nameof(MechanicalProperties.HardnessScale), m.HardnessScale, v => v);

        AddMeasurements(stored, cert);

        return stored;
    }

    /// <summary>
    /// Measurements: the final ones become CertificateMeasurement rows; every one that has a
    /// value OR was edited (including removed) gets an evidence row named
    /// "Measurement:&lt;key&gt;" holding the model's ORIGINAL rendered as "value unit (condition)".
    /// A removed measurement therefore leaves evidence but no measurement row.
    /// </summary>
    private static void AddMeasurements(StoredCertificate stored, SteelCertificate cert)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var measurement in cert.Measurements)
        {
            // One row per property. (The store rejects duplicates before we get here;
            // this keeps the unique indexes safe regardless.)
            if (!seen.Add(measurement.EvidenceName)) continue;

            var value = measurement.Field.Value;
            if (value?.Value is not null)
            {
                stored.Measurements.Add(new CertificateMeasurement
                {
                    Key = measurement.Key,
                    Label = measurement.Label,
                    Category = measurement.Category,
                    Value = value.Value,
                    Unit = value.Unit,
                    Condition = value.Condition,
                });
            }

            AddEvidence(stored, measurement.EvidenceName, measurement.Field, v => v.Display);
        }
    }

    private static void AddDecimalEvidence(
        StoredCertificate stored, string fieldName, ExtractedField<decimal?>? field) =>
        AddEvidence(stored, fieldName, field,
            v => v!.Value.ToString(CultureInfo.InvariantCulture));

    // "Has a value": non-null, and for text not blank.
    private static bool HasValue<T>(T? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        MeasurementValue measurement => measurement.Value is not null,
        _ => true,
    };

    private static void AddEvidence<T>(
        StoredCertificate stored,
        string fieldName,
        ExtractedField<T>? field,
        Func<T, string> toText)
    {
        if (field is null) return;

        var hasValue = HasValue(field.Value);
        var edited = field.IsEdited;

        // A field the model left empty and nobody touched leaves no row. A value
        // the reviewer cleared (edited to empty) DOES leave one: the original
        // text is the only trace of what the model had said.
        if (!hasValue && !edited) return;

        stored.Evidence.Add(new CertificateFieldEvidence
        {
            FieldName = fieldName,
            // What the MODEL extracted (NULL if nothing); the final value is in the certificate column.
            ExtractedValueText = HasValue(field.OriginalValue) ? toText(field.OriginalValue!) : null,
            Page = field.PageNumber,
            BboxX = field.BoundingBox?.X,
            BboxY = field.BoundingBox?.Y,
            BboxW = field.BoundingBox?.W,
            BboxH = field.BoundingBox?.H,
            Confirmed = field.Confirmed,
            Edited = edited,
        });
    }
}
