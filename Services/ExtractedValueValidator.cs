using System.Globalization;
using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Services;

public enum FieldKind
{
    Text,
    Decimal,
    Date,
}

/// <summary>
/// Validation rule for one extractable field. <see cref="Key"/> is the property
/// name on the extraction model (the same name used for evidence rows).
/// </summary>
public sealed record FieldRule(
    string Key,
    string Label,
    FieldKind Kind,
    bool Required = false,
    int MaxLength = 0,              // text: must equal the DB column size
    decimal? Min = null,            // decimal range
    decimal? Max = null,
    bool MinExclusive = false);

/// <summary>Result of validating reviewer input for one field.</summary>
public sealed record ValidationOutcome(
    bool IsValid,
    string? Error,
    bool IsCleared,          // valid, and the reviewer emptied an optional field
    string? Text,
    decimal? Number,
    DateOnly? Date)
{
    public static ValidationOutcome Fail(string error) => new(false, error, false, null, null, null);
}

/// <summary>
/// One place for "what is a sane value for this field", used by the review
/// screen (inline errors) AND the store (defense in depth). Pure — no I/O.
///
/// Rules: chemistry % and elongation 0–100; yield/tensile strength &gt; 0 and
/// ≤ 1,000,000 psi; dates yyyy-MM-dd between 1950-01-01 and today (+1 day to
/// tolerate time zones); Heat Number and Grade required; text max lengths equal
/// the database column sizes so over-long text can't cause a SQL error.
/// </summary>
public static class ExtractedValueValidator
{
    public static readonly DateOnly EarliestDate = new(1950, 1, 1);

    private const decimal MaxStrengthPsi = 1_000_000m;
    private const decimal MaxHardness = 2000m;   // covers HB, HRC, HV and the like

    public static IReadOnlyList<FieldRule> Rules { get; } = BuildRules();

    private static readonly Dictionary<string, FieldRule> ByKey = Rules.ToDictionary(r => r.Key);

    public static FieldRule RuleFor(string key) =>
        ByKey.TryGetValue(key, out var rule)
            ? rule
            : throw new ArgumentException($"No validation rule for field '{key}'.", nameof(key));

    // --- Reviewer input (text typed into the edit box) ---

    /// <summary>Parses and validates what the reviewer typed. Blank = "clear" (error if the field is required).</summary>
    public static ValidationOutcome Validate(string key, string? input, DateOnly? today = null)
    {
        var rule = RuleFor(key);
        var text = input?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return rule.Required
                ? ValidationOutcome.Fail($"{rule.Label} is required.")
                : new ValidationOutcome(true, null, IsCleared: true, null, null, null);
        }

        switch (rule.Kind)
        {
            case FieldKind.Text:
            {
                var error = CheckText(key, text);
                return error is null
                    ? new ValidationOutcome(true, null, false, text, null, null)
                    : ValidationOutcome.Fail(error);
            }
            case FieldKind.Decimal:
            {
                if (!decimal.TryParse(text,
                        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number))
                {
                    return ValidationOutcome.Fail($"{rule.Label} must be a number (use . for decimals).");
                }
                var error = CheckNumber(key, number);
                return error is null
                    ? new ValidationOutcome(true, null, false, null, number, null)
                    : ValidationOutcome.Fail(error);
            }
            default: // Date
            {
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                {
                    return ValidationOutcome.Fail($"{rule.Label} must be a date in YYYY-MM-DD format.");
                }
                var error = CheckDate(key, date, today);
                return error is null
                    ? new ValidationOutcome(true, null, false, null, null, date)
                    : ValidationOutcome.Fail(error);
            }
        }
    }

    // --- Typed final values (used by the store) — return an error message, or null when fine ---

    public static string? CheckText(string key, string? value)
    {
        var rule = RuleFor(key);
        var text = value?.Trim() ?? string.Empty;

        if (text.Length == 0)
        {
            return rule.Required ? $"{rule.Label} is required." : null;
        }
        if (rule.MaxLength > 0 && text.Length > rule.MaxLength)
        {
            return $"{rule.Label} must be at most {rule.MaxLength} characters.";
        }
        return null;
    }

    public static string? CheckNumber(string key, decimal? value)
    {
        var rule = RuleFor(key);
        if (value is null) return rule.Required ? $"{rule.Label} is required." : null;

        var v = value.Value;
        var belowMin = rule.Min is { } min && (rule.MinExclusive ? v <= min : v < min);
        var aboveMax = rule.Max is { } max && v > max;
        if (!belowMin && !aboveMax) return null;

        return rule.MinExclusive
            ? $"{rule.Label} must be greater than {Format(rule.Min!.Value)} and at most {Format(rule.Max!.Value)}."
            : $"{rule.Label} must be between {Format(rule.Min!.Value)} and {Format(rule.Max!.Value)}.";
    }

    /// <summary>Dates may be up to one day ahead of today (UTC) so a reviewer east of UTC isn't wrongly rejected.</summary>
    public static string? CheckDate(string key, DateOnly? value, DateOnly? today = null)
    {
        var rule = RuleFor(key);
        if (value is null) return rule.Required ? $"{rule.Label} is required." : null;

        var latest = (today ?? DateOnly.FromDateTime(DateTime.UtcNow)).AddDays(1);
        if (value.Value < EarliestDate || value.Value > latest)
        {
            return $"{rule.Label} must be between {EarliestDate:yyyy-MM-dd} and today.";
        }
        return null;
    }

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static List<FieldRule> BuildRules()
    {
        var rules = new List<FieldRule>
        {
            new(nameof(SteelCertificate.HeatNumber), "Heat Number", FieldKind.Text, Required: true, MaxLength: 100),
            new(nameof(SteelCertificate.Grade), "Grade", FieldKind.Text, Required: true, MaxLength: 100),
            new(nameof(SteelCertificate.Specification), "Specification", FieldKind.Text, MaxLength: 200),
            new(nameof(SteelCertificate.CertifyingLab), "Certifying Lab", FieldKind.Text, MaxLength: 200),
            new(nameof(SteelCertificate.CertDate), "Cert Date", FieldKind.Date),
            new(nameof(SteelCertificate.PurchaseOrderNumber), "PO Number", FieldKind.Text, MaxLength: 100),

            new(nameof(MechanicalProperties.YieldStrengthPsi), "Yield Strength (psi)", FieldKind.Decimal,
                Min: 0m, Max: MaxStrengthPsi, MinExclusive: true),
            new(nameof(MechanicalProperties.TensileStrengthPsi), "Tensile Strength (psi)", FieldKind.Decimal,
                Min: 0m, Max: MaxStrengthPsi, MinExclusive: true),
            new(nameof(MechanicalProperties.ElongationPct), "Elongation (%)", FieldKind.Decimal, Min: 0m, Max: 100m),
            new(nameof(MechanicalProperties.ReductionOfAreaPct), "Reduction of Area", FieldKind.Text, MaxLength: 50),
            new(nameof(MechanicalProperties.HardnessValue), "Hardness", FieldKind.Decimal,
                Min: 0m, Max: MaxHardness, MinExclusive: true),
            new(nameof(MechanicalProperties.HardnessScale), "Hardness Scale", FieldKind.Text, MaxLength: 20),
        };

        void Chem(string key, string label) =>
            rules.Add(new FieldRule(key, label, FieldKind.Decimal, Min: 0m, Max: 100m));

        Chem(nameof(ChemistryComposition.CarbonPct), "Carbon");
        Chem(nameof(ChemistryComposition.ManganesePct), "Manganese");
        Chem(nameof(ChemistryComposition.PhosphorusPct), "Phosphorus");
        Chem(nameof(ChemistryComposition.SulfurPct), "Sulfur");
        Chem(nameof(ChemistryComposition.SiliconPct), "Silicon");
        Chem(nameof(ChemistryComposition.ChromiumPct), "Chromium");
        Chem(nameof(ChemistryComposition.NickelPct), "Nickel");
        Chem(nameof(ChemistryComposition.MolybdenumPct), "Molybdenum");
        Chem(nameof(ChemistryComposition.CopperPct), "Copper");
        Chem(nameof(ChemistryComposition.VanadiumPct), "Vanadium");
        Chem(nameof(ChemistryComposition.NitrogenPct), "Nitrogen");
        Chem(nameof(ChemistryComposition.CobaltPct), "Cobalt");
        Chem(nameof(ChemistryComposition.BoronPct), "Boron");
        Chem(nameof(ChemistryComposition.AluminumPct), "Aluminum");
        Chem(nameof(ChemistryComposition.TitaniumPct), "Titanium");
        Chem(nameof(ChemistryComposition.NiobiumPct), "Niobium");
        Chem(nameof(ChemistryComposition.TungstenPct), "Tungsten");
        Chem(nameof(ChemistryComposition.TinPct), "Tin");
        Chem(nameof(ChemistryComposition.LeadPct), "Lead");
        Chem(nameof(ChemistryComposition.ArsenicPct), "Arsenic");
        Chem(nameof(ChemistryComposition.AntimonyPct), "Antimony");
        Chem(nameof(ChemistryComposition.CalciumPct), "Calcium");
        Chem(nameof(ChemistryComposition.OxygenPct), "Oxygen");
        Chem(nameof(ChemistryComposition.HydrogenPct), "Hydrogen");
        Chem(nameof(ChemistryComposition.TantalumPct), "Tantalum");
        Chem(nameof(ChemistryComposition.CarbonEquivalent), "Carbon Equivalent");

        return rules;
    }
}
