using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class MeasurementModelTests
{
    // --- catalog ---

    [Fact]
    public void The_catalog_has_the_22_specified_keys_with_unique_snake_case_names()
    {
        var kinds = MeasurementCatalog.Kinds;

        Assert.Equal(22, kinds.Count);
        Assert.Equal(kinds.Count, kinds.Select(k => k.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(kinds, k => Assert.Matches("^[a-z]+(_[a-z0-9]+)*$", k.Key));
    }

    [Fact]
    public void Every_kind_has_a_label_and_a_known_category_and_the_other_key_is_reserved()
    {
        Assert.All(MeasurementCatalog.Kinds, k =>
        {
            Assert.False(string.IsNullOrWhiteSpace(k.Label));
            Assert.Contains(k.Category, MeasurementCatalog.Categories);
            Assert.NotEqual(MeasurementCatalog.OtherKey, k.Key);
            Assert.True(k.Label.Length <= MeasurementLimits.Label);
            Assert.True(k.Key.Length <= MeasurementLimits.Key);
        });
    }

    [Theory]
    [InlineData("Impact", 5)]
    [InlineData("Tensile", 4)]
    [InlineData("HeatTreatment", 7)]
    [InlineData("Product", 6)]
    public void Categories_hold_the_specified_number_of_keys(string category, int count)
    {
        Assert.Equal(count, MeasurementCatalog.Kinds.Count(k => k.Category == category));
    }

    [Theory]
    [InlineData("impact_energy", "Impact energy")]
    [InlineData("IMPACT_ENERGY", "Impact energy")]
    [InlineData("  grain_size ", "Grain size")]
    [InlineData("mill_name", "Mill name")]
    public void Find_is_case_and_whitespace_insensitive(string key, string label)
    {
        Assert.Equal(label, MeasurementCatalog.Find(key)!.Label);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("magnetic_permeability")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_returns_null_for_non_catalog_keys(string? key)
    {
        Assert.Null(MeasurementCatalog.Find(key));
    }

    [Theory]
    [InlineData("Impact", "Impact")]
    [InlineData("HeatTreatment", "Heat treatment & process")]
    [InlineData("Product", "Product & traceability")]
    [InlineData("Tensile", "Tensile details")]
    [InlineData("Other", "Other")]
    [InlineData("whatever", "Other")]
    public void Category_labels_are_readable(string category, string label)
    {
        Assert.Equal(label, MeasurementCatalog.CategoryLabel(category));
    }

    // --- MeasurementValue ---

    [Fact]
    public void Blank_parts_are_normalized_to_null_and_text_is_trimmed()
    {
        var value = new MeasurementValue("  54 ", "  ", "");

        Assert.Equal("54", value.Value);
        Assert.Null(value.Unit);
        Assert.Null(value.Condition);
    }

    [Fact]
    public void Values_compare_by_content()
    {
        Assert.Equal(new MeasurementValue("54", "J", "-40 C"), new MeasurementValue(" 54", "J ", " -40 C"));
        Assert.NotEqual(new MeasurementValue("54", "J"), new MeasurementValue("55", "J"));
        Assert.NotEqual(new MeasurementValue("54", "J"), new MeasurementValue("54", "ft-lbf"));
        Assert.Equal(new MeasurementValue("54", ""), new MeasurementValue("54", null));
    }

    [Theory]
    [InlineData("54", "J", "-40 C, 10x10 mm", "54 J (-40 C, 10x10 mm)")]
    [InlineData("54", "J", null, "54 J")]
    [InlineData("6-8", null, null, "6-8")]
    [InlineData("Round bar", null, "annealed", "Round bar (annealed)")]
    [InlineData("1900", "F", "min", "1900 F (min)")]
    public void Display_combines_value_unit_and_condition(string value, string? unit, string? condition, string expected)
    {
        Assert.Equal(expected, new MeasurementValue(value, unit, condition).Display);
    }

    [Fact]
    public void A_measurement_field_is_edited_when_any_part_changes_and_not_when_restored()
    {
        var original = new MeasurementValue("54", "J", "-40 C");
        var field = new ExtractedField<MeasurementValue> { Value = original, OriginalValue = original };
        Assert.False(field.IsEdited);

        field.Value = new MeasurementValue("54", "J", "-20 C");   // condition only
        Assert.True(field.IsEdited);

        field.Value = original;
        Assert.False(field.IsEdited);

        field.Value = null;                                        // removed
        Assert.True(field.IsEdited);
    }

    [Fact]
    public void An_added_measurement_has_no_original_and_is_edited()
    {
        var field = new ExtractedField<MeasurementValue> { Value = new MeasurementValue("6-8"), OriginalValue = null };

        Assert.True(field.IsEdited);
    }

    [Fact]
    public void Evidence_names_are_unique_per_key_and_per_other_label()
    {
        var impact = new ExtractedMeasurement { Key = "impact_energy", Label = "Impact energy" };
        var deltaFerrite = new ExtractedMeasurement { Key = "other", Label = "Delta ferrite" };

        Assert.Equal("Measurement:impact_energy", impact.EvidenceName);
        Assert.Equal("Measurement:other:Delta ferrite", deltaFerrite.EvidenceName);
    }

    [Fact]
    public void The_longest_possible_evidence_name_fits_the_evidence_column()
    {
        using var db = new CertDbContext(new DbContextOptionsBuilder<CertDbContext>().UseSqlite("DataSource=:memory:").Options);
        var column = db.Model.FindEntityType(typeof(CertificateFieldEvidence))!
            .FindProperty(nameof(CertificateFieldEvidence.FieldName))!.GetMaxLength()!.Value;
        var longest = new ExtractedMeasurement { Key = "other", Label = new string('L', MeasurementLimits.Label) }.EvidenceName;

        Assert.True(longest.Length <= column, $"{longest.Length} > {column}");
    }

    // --- limits match the database ---

    [Fact]
    public void Measurement_limits_match_the_database_column_sizes()
    {
        using var db = new CertDbContext(new DbContextOptionsBuilder<CertDbContext>().UseSqlite("DataSource=:memory:").Options);
        var entity = db.Model.FindEntityType(typeof(CertificateMeasurement))!;

        Assert.Equal(MeasurementLimits.Key, entity.FindProperty(nameof(CertificateMeasurement.Key))!.GetMaxLength());
        Assert.Equal(MeasurementLimits.Label, entity.FindProperty(nameof(CertificateMeasurement.Label))!.GetMaxLength());
        Assert.Equal(MeasurementLimits.Category, entity.FindProperty(nameof(CertificateMeasurement.Category))!.GetMaxLength());
        Assert.Equal(MeasurementLimits.Value, entity.FindProperty(nameof(CertificateMeasurement.Value))!.GetMaxLength());
        Assert.Equal(MeasurementLimits.Unit, entity.FindProperty(nameof(CertificateMeasurement.Unit))!.GetMaxLength());
        Assert.Equal(MeasurementLimits.Condition, entity.FindProperty(nameof(CertificateMeasurement.Condition))!.GetMaxLength());
    }

    // --- validator: reviewer input ---

    [Fact]
    public void Valid_input_returns_a_cleaned_value()
    {
        var outcome = MeasurementValidator.ValidateInput(" 54 ", "J", " -40 C ");

        Assert.True(outcome.IsValid);
        Assert.Equal(new MeasurementValue("54", "J", "-40 C"), outcome.Result);
        Assert.Null(outcome.ValueError);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_value_is_required(string? value)
    {
        var outcome = MeasurementValidator.ValidateInput(value, "J", null);

        Assert.False(outcome.IsValid);
        Assert.Equal("Value is required.", outcome.ValueError);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public void Unit_and_condition_are_optional()
    {
        Assert.True(MeasurementValidator.ValidateInput("6-8", null, null).IsValid);
        Assert.True(MeasurementValidator.ValidateInput("6-8", "", "").IsValid);
    }

    [Fact]
    public void Each_part_is_limited_to_its_column_size_and_errors_are_reported_separately()
    {
        var atLimit = MeasurementValidator.ValidateInput(
            new string('v', MeasurementLimits.Value), new string('u', MeasurementLimits.Unit), new string('c', MeasurementLimits.Condition));
        Assert.True(atLimit.IsValid);

        var over = MeasurementValidator.ValidateInput(
            new string('v', MeasurementLimits.Value + 1), new string('u', MeasurementLimits.Unit + 1), new string('c', MeasurementLimits.Condition + 1));
        Assert.False(over.IsValid);
        Assert.Equal("Value must be at most 200 characters.", over.ValueError);
        Assert.Equal("Unit must be at most 20 characters.", over.UnitError);
        Assert.Equal("Condition must be at most 200 characters.", over.ConditionError);
    }

    [Theory]
    [InlineData("Delta ferrite", null)]
    [InlineData("  Delta ferrite  ", null)]
    [InlineData("", "Label is required.")]
    [InlineData("   ", "Label is required.")]
    [InlineData(null, "Label is required.")]
    public void Custom_labels_are_required(string? label, string? expectedError)
    {
        Assert.Equal(expectedError, MeasurementValidator.CheckCustomLabel(label));
    }

    [Fact]
    public void Custom_labels_are_limited_to_100_characters()
    {
        Assert.Null(MeasurementValidator.CheckCustomLabel(new string('L', 100)));
        Assert.Equal("Label must be at most 100 characters.", MeasurementValidator.CheckCustomLabel(new string('L', 101)));
    }

    // --- validator: a measurement about to be saved ---

    private static ExtractedMeasurement Measurement(string key = "impact_energy", string label = "Impact energy", MeasurementValue? value = null) =>
        new()
        {
            Key = key,
            Label = label,
            Category = "Impact",
            Field = new ExtractedField<MeasurementValue> { Value = value, OriginalValue = value },
        };

    [Fact]
    public void A_good_measurement_has_no_errors()
    {
        Assert.Empty(MeasurementValidator.Check(Measurement(value: new MeasurementValue("54", "J", "-40 C"))));
    }

    [Fact]
    public void A_removed_measurement_with_no_value_is_not_an_error()
    {
        Assert.Empty(MeasurementValidator.Check(Measurement(value: null)));
    }

    [Fact]
    public void Errors_name_the_measurement()
    {
        var errors = MeasurementValidator.Check(Measurement(value: new MeasurementValue(new string('v', 201), new string('u', 21), null)));

        Assert.Contains("Impact energy: value must be at most 200 characters.", errors);
        Assert.Contains("Impact energy: unit must be at most 20 characters.", errors);
    }

    [Fact]
    public void A_custom_measurement_without_a_label_is_an_error()
    {
        var errors = MeasurementValidator.Check(Measurement(key: "other", label: "  ", value: new MeasurementValue("5")));

        Assert.Contains(errors, e => e.Contains("label is required"));
    }
}
