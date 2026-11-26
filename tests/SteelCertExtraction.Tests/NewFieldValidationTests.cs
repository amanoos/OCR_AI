using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

/// <summary>Rules for the "more properties" typed fields (the guard tests in
/// ExtractedValueValidatorTests already prove every field has a rule and that
/// text rule lengths match the database columns).</summary>
public class NewFieldValidationTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    public static IEnumerable<object[]> NewElements() => new[]
    {
        new object[] { "NitrogenPct", "Nitrogen" },
        new object[] { "CobaltPct", "Cobalt" },
        new object[] { "BoronPct", "Boron" },
        new object[] { "AluminumPct", "Aluminum" },
        new object[] { "TitaniumPct", "Titanium" },
        new object[] { "NiobiumPct", "Niobium" },
        new object[] { "TungstenPct", "Tungsten" },
        new object[] { "TinPct", "Tin" },
        new object[] { "LeadPct", "Lead" },
        new object[] { "ArsenicPct", "Arsenic" },
        new object[] { "AntimonyPct", "Antimony" },
        new object[] { "CalciumPct", "Calcium" },
        new object[] { "OxygenPct", "Oxygen" },
        new object[] { "HydrogenPct", "Hydrogen" },
        new object[] { "TantalumPct", "Tantalum" },
        new object[] { "CarbonEquivalent", "Carbon Equivalent" },
    };

    [Theory]
    [MemberData(nameof(NewElements))]
    public void Each_new_element_is_a_decimal_0_to_100_with_its_own_label(string key, string label)
    {
        var rule = ExtractedValueValidator.RuleFor(key);
        Assert.Equal(FieldKind.Decimal, rule.Kind);
        Assert.Equal(label, rule.Label);

        Assert.True(ExtractedValueValidator.Validate(key, "0", Today).IsValid);
        Assert.True(ExtractedValueValidator.Validate(key, "0.0074", Today).IsValid);
        Assert.True(ExtractedValueValidator.Validate(key, "100", Today).IsValid);
        Assert.Equal($"{label} must be between 0 and 100.", ExtractedValueValidator.Validate(key, "100.01", Today).Error);
        Assert.Equal($"{label} must be between 0 and 100.", ExtractedValueValidator.Validate(key, "-0.1", Today).Error);
        Assert.Contains("must be a number", ExtractedValueValidator.Validate(key, "<0.001", Today).Error);
    }

    [Theory]
    [InlineData("163")]
    [InlineData("0.01")]
    [InlineData("2000")]
    [InlineData(" 58.5 ")]
    public void Valid_hardness_values_are_accepted(string input)
    {
        Assert.True(ExtractedValueValidator.Validate("HardnessValue", input, Today).IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("2000.01")]
    public void Out_of_range_hardness_is_rejected(string input)
    {
        var result = ExtractedValueValidator.Validate("HardnessValue", input, Today);

        Assert.False(result.IsValid);
        Assert.Equal("Hardness must be greater than 0 and at most 2000.", result.Error);
    }

    [Fact]
    public void Hardness_scale_is_free_text_up_to_20_characters_and_optional()
    {
        Assert.True(ExtractedValueValidator.Validate("HardnessScale", "HB", Today).IsValid);
        Assert.True(ExtractedValueValidator.Validate("HardnessScale", new string('X', 20), Today).IsValid);
        Assert.True(ExtractedValueValidator.Validate("HardnessScale", "", Today).IsCleared);

        var tooLong = ExtractedValueValidator.Validate("HardnessScale", new string('X', 21), Today);
        Assert.Equal("Hardness Scale must be at most 20 characters.", tooLong.Error);
    }

    [Fact]
    public void New_fields_are_all_optional()
    {
        foreach (var rule in ExtractedValueValidator.Rules.Where(r => r.Key is "HardnessValue" or "HardnessScale"
                     || r.Key.EndsWith("Pct") || r.Key == "CarbonEquivalent"))
        {
            Assert.False(rule.Required, rule.Key);
        }
    }
}
