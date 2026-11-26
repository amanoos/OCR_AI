using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Services;

/// <summary>
/// The rules for editing a certificate's "other measurements" on the review screen:
/// what can be added, how an edit/removal/revert changes the state, and what stays
/// visible. Pure logic on the in-memory certificate (no UI, no I/O) so it is unit-tested;
/// the page only calls it.
///
/// The invariants it keeps: a measurement the MODEL extracted keeps its original value
/// forever (so it can be reverted and audited); a measurement a REVIEWER added has no
/// original; any edit un-confirms; there is at most one measurement per catalog key and
/// one "other" per label.
/// </summary>
public static class MeasurementEditor
{
    public static bool HasValue(ExtractedMeasurement measurement) => measurement.Field.Value?.Value is not null;

    private static bool IsOther(ExtractedMeasurement measurement) => measurement.Key == MeasurementCatalog.OtherKey;

    private static ExtractedMeasurement NewMeasurement(string key, string label, string category) => new()
    {
        Key = key,
        Label = label,
        Category = category,
        // Added by a reviewer: no original, page 0, no highlight box.
        Field = new ExtractedField<MeasurementValue> { Value = null, OriginalValue = null },
    };

    /// <summary>Catalog kinds the reviewer can still add: those not currently listed with a value.</summary>
    public static IReadOnlyList<MeasurementKind> AvailableKinds(SteelCertificate cert)
    {
        var present = cert.Measurements
            .Where(m => HasValue(m) && !IsOther(m))
            .Select(m => m.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return MeasurementCatalog.Kinds.Where(k => !present.Contains(k.Key)).ToList();
    }

    public static bool CanAddOther(SteelCertificate cert) =>
        cert.Measurements.Count(m => IsOther(m) && HasValue(m)) < MeasurementCatalog.MaxOtherPerCertificate;

    /// <summary>
    /// Starts adding a measurement of the chosen type (a catalog key, or "other" with a label) and
    /// returns it, ready for the reviewer to type its value. If that property was extracted and then
    /// removed, the SAME measurement is returned so its original is preserved. Returns an error
    /// message instead when the choice isn't allowed.
    /// </summary>
    public static (ExtractedMeasurement? Measurement, string? Error) Add(
        SteelCertificate cert, string? key, string? customLabel)
    {
        key = key?.Trim() ?? string.Empty;
        if (key.Length == 0) return (null, "Choose a measurement type.");

        if (string.Equals(key, MeasurementCatalog.OtherKey, StringComparison.OrdinalIgnoreCase))
        {
            var labelError = MeasurementValidator.CheckCustomLabel(customLabel);
            if (labelError is not null) return (null, labelError);
            var label = customLabel!.Trim();

            var existing = cert.Measurements.FirstOrDefault(m =>
                IsOther(m) && string.Equals(m.Label, label, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && HasValue(existing)) return (null, $"\"{label}\" is already listed.");
            if (existing is not null) return (existing, null);

            if (!CanAddOther(cert))
            {
                return (null, $"At most {MeasurementCatalog.MaxOtherPerCertificate} custom measurements.");
            }

            var created = NewMeasurement(MeasurementCatalog.OtherKey, label, MeasurementCatalog.OtherCategory);
            cert.Measurements.Add(created);
            return (created, null);
        }

        var kind = MeasurementCatalog.Find(key);
        if (kind is null) return (null, "Unknown measurement type.");

        var present = cert.Measurements.FirstOrDefault(m =>
            string.Equals(m.Key, kind.Key, StringComparison.OrdinalIgnoreCase));
        if (present is not null && HasValue(present)) return (null, $"{kind.Label} is already listed.");
        if (present is not null) return (present, null);

        var added = NewMeasurement(kind.Key, kind.Label, kind.Category);
        cert.Measurements.Add(added);
        return (added, null);
    }

    /// <summary>
    /// Validates and applies what the reviewer typed. Invalid input changes nothing. A changed value
    /// un-confirms (it must be ticked again); typing the same value leaves the confirmation alone.
    /// </summary>
    public static MeasurementInputOutcome Apply(
        ExtractedMeasurement measurement, string? value, string? unit, string? condition)
    {
        var outcome = MeasurementValidator.ValidateInput(value, unit, condition);
        if (!outcome.IsValid) return outcome;

        if (!EqualityComparer<MeasurementValue?>.Default.Equals(measurement.Field.Value, outcome.Result))
        {
            measurement.Field.Value = outcome.Result;
            measurement.Field.Confirmed = false;
        }
        return outcome;
    }

    /// <summary>
    /// Removes a (wrongly extracted) measurement: clears its value. One the model extracted stays in
    /// the list as "removed" so it can be reverted and so evidence of the original is kept; one the
    /// reviewer added is dropped entirely.
    /// </summary>
    public static void Remove(SteelCertificate cert, ExtractedMeasurement measurement)
    {
        measurement.Field.Value = null;
        measurement.Field.Confirmed = false;
        DiscardIfEmpty(cert, measurement);
    }

    /// <summary>Restores what the model extracted (no-op for a measurement the reviewer added); un-confirms.</summary>
    public static void Revert(ExtractedMeasurement measurement)
    {
        if (measurement.Field.OriginalValue is null) return;

        measurement.Field.Value = measurement.Field.OriginalValue;
        measurement.Field.Confirmed = false;
    }

    /// <summary>Drops a measurement that has neither a value nor an original (e.g. an add that was cancelled).</summary>
    public static void DiscardIfEmpty(SteelCertificate cert, ExtractedMeasurement measurement)
    {
        if (!HasValue(measurement) && measurement.Field.OriginalValue?.Value is null)
        {
            cert.Measurements.Remove(measurement);
        }
    }

    /// <summary>
    /// What the screen lists, in category order (Impact, Tensile, Heat treatment, Product, Other) and
    /// then the model's order: measurements with a value, removed ones that can be reverted, and any
    /// being edited right now (<paramref name="open"/>).
    /// </summary>
    public static IEnumerable<ExtractedMeasurement> Ordered(
        SteelCertificate cert, IReadOnlyCollection<ExtractedMeasurement>? open = null)
    {
        var categories = MeasurementCatalog.Categories.ToList();

        return cert.Measurements
            .Select((measurement, index) => (measurement, index))
            .Where(x => HasValue(x.measurement)
                        || x.measurement.Field.OriginalValue?.Value is not null
                        || (open?.Contains(x.measurement) ?? false))
            .OrderBy(x =>
            {
                var position = categories.IndexOf(x.measurement.Category);
                return position < 0 ? categories.Count : position;
            })
            .ThenBy(x => x.index)
            .Select(x => x.measurement);
    }
}
