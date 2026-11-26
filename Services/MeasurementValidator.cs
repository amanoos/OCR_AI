using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Services;

/// <summary>Text-length limits for measurements — equal to the CertificateMeasurements column sizes.</summary>
public static class MeasurementLimits
{
    public const int Key = 50;
    public const int Label = 100;
    public const int Category = 50;
    public const int Value = 200;
    public const int Unit = 20;
    public const int Condition = 200;
}

/// <summary>Result of validating what a reviewer typed for a measurement's three parts.</summary>
public sealed record MeasurementInputOutcome(
    bool IsValid,
    string? ValueError,
    string? UnitError,
    string? ConditionError,
    MeasurementValue? Result);

/// <summary>
/// Rules for "other measurements". Their shape varies, so there are no numeric range
/// checks — only: a value is required, and every part fits its column. Used by the
/// review screen (inline errors) and by the store (defense in depth). Pure — no I/O.
/// </summary>
public static class MeasurementValidator
{
    /// <summary>Validates the three inputs typed into a measurement's editor.</summary>
    public static MeasurementInputOutcome ValidateInput(string? value, string? unit, string? condition)
    {
        var result = new MeasurementValue(value, unit, condition);

        var valueError = result.Value is null
            ? "Value is required."
            : TooLong(result.Value, MeasurementLimits.Value, "Value");
        var unitError = TooLong(result.Unit, MeasurementLimits.Unit, "Unit");
        var conditionError = TooLong(result.Condition, MeasurementLimits.Condition, "Condition");

        var ok = valueError is null && unitError is null && conditionError is null;
        return new MeasurementInputOutcome(ok, valueError, unitError, conditionError, ok ? result : null);
    }

    /// <summary>Validates the label a reviewer types for a custom ("other") measurement.</summary>
    public static string? CheckCustomLabel(string? label)
    {
        var text = label?.Trim() ?? string.Empty;
        if (text.Length == 0) return "Label is required.";
        return TooLong(text, MeasurementLimits.Label, "Label");
    }

    /// <summary>
    /// Every problem with a measurement about to be saved (empty list = fine). A measurement
    /// whose value was removed is not saved at all, so a null value is not an error here.
    /// </summary>
    public static List<string> Check(ExtractedMeasurement measurement)
    {
        var errors = new List<string>();
        var value = measurement.Field.Value;
        if (value is null) return errors;

        var name = string.IsNullOrWhiteSpace(measurement.Label) ? measurement.Key : measurement.Label;

        void Add(string? error)
        {
            if (error is not null) errors.Add($"{name}: {char.ToLowerInvariant(error[0])}{error[1..]}");
        }

        if (measurement.Key.Length == 0) errors.Add("A measurement has no key.");
        else if (measurement.Key.Length > MeasurementLimits.Key) errors.Add($"{name}: key is too long.");

        Add(CheckCustomLabel(measurement.Label));
        var input = ValidateInput(value.Value, value.Unit, value.Condition);
        Add(input.ValueError);
        Add(input.UnitError);
        Add(input.ConditionError);
        return errors;
    }

    private static string? TooLong(string? text, int max, string what) =>
        text is not null && text.Length > max ? $"{what} must be at most {max} characters." : null;
}
