using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class ExtractedValueValidatorTests
{
    private static readonly DateOnly Today = new(2026, 11, 26);

    // --- the rule table itself ---

    private static IEnumerable<string> ExtractableFieldNames() =>
        new[] { typeof(SteelCertificate), typeof(ChemistryComposition), typeof(MechanicalProperties) }
            .SelectMany(t => t.GetProperties())
            .Where(p => p.PropertyType.IsGenericType
                        && p.PropertyType.GetGenericTypeDefinition() == typeof(ExtractedField<>))
            .Select(p => p.Name);

    [Fact]
    public void Every_extractable_field_has_a_rule_and_there_are_no_extras()
    {
        // Guards against adding a field to the model without deciding how it is validated.
        var fields = ExtractableFieldNames().OrderBy(n => n).ToList();
        var rules = ExtractedValueValidator.Rules.Select(r => r.Key).OrderBy(n => n).ToList();

        Assert.Equal(fields, rules);
    }

    [Fact]
    public void Text_max_lengths_match_the_database_column_sizes()
    {
        var options = new DbContextOptionsBuilder<CertDbContext>().UseSqlite("DataSource=:memory:").Options;
        using var db = new CertDbContext(options);
        var entity = db.Model.FindEntityType(typeof(StoredCertificate))!;

        foreach (var rule in ExtractedValueValidator.Rules.Where(r => r.Kind == FieldKind.Text))
        {
            var column = entity.FindProperty(rule.Key);
            Assert.NotNull(column);
            Assert.Equal(column!.GetMaxLength(), rule.MaxLength);
        }
    }

    [Fact]
    public void Unknown_field_throws()
    {
        Assert.Throws<ArgumentException>(() => ExtractedValueValidator.Validate("NoSuchField", "1"));
    }

    // --- numbers ---

    [Theory]
    [InlineData("CarbonPct", "0.014", 0.014)]
    [InlineData("CarbonPct", "  0.014  ", 0.014)]
    [InlineData("CarbonPct", "0", 0)]
    [InlineData("CarbonPct", "100", 100)]
    [InlineData("ChromiumPct", "18.02", 18.02)]
    [InlineData("ElongationPct", "60.6", 60.6)]
    [InlineData("YieldStrengthPsi", "34910", 34910)]
    [InlineData("YieldStrengthPsi", "0.01", 0.01)]
    [InlineData("TensileStrengthPsi", "1000000", 1000000)]
    public void Valid_numbers_are_accepted(string key, string input, double expected)
    {
        var result = ExtractedValueValidator.Validate(key, input, Today);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal((decimal)expected, result.Number);
        Assert.False(result.IsCleared);
    }

    [Theory]
    [InlineData("CarbonPct", "abc")]
    [InlineData("CarbonPct", "0,014")]       // comma decimal not accepted
    [InlineData("CarbonPct", "1e1")]         // no exponents
    [InlineData("CarbonPct", "<0.01")]       // mill-cert style, not a number
    [InlineData("CarbonPct", "1,000")]       // no thousands separators
    [InlineData("CarbonPct", "NaN")]
    public void Non_numbers_are_rejected_with_a_message_naming_the_field(string key, string input)
    {
        var result = ExtractedValueValidator.Validate(key, input, Today);

        Assert.False(result.IsValid);
        Assert.Contains("must be a number", result.Error);
        Assert.Contains(ExtractedValueValidator.RuleFor(key).Label, result.Error);
    }

    [Theory]
    [InlineData("CarbonPct", "-0.001")]
    [InlineData("CarbonPct", "100.0001")]
    [InlineData("VanadiumPct", "150")]
    [InlineData("ElongationPct", "100.5")]
    [InlineData("YieldStrengthPsi", "0")]            // strengths must be > 0
    [InlineData("YieldStrengthPsi", "-5")]
    [InlineData("TensileStrengthPsi", "1000000.01")]
    public void Out_of_range_numbers_are_rejected(string key, string input)
    {
        var result = ExtractedValueValidator.Validate(key, input, Today);

        Assert.False(result.IsValid);
        Assert.Contains(ExtractedValueValidator.RuleFor(key).Label, result.Error);
        Assert.Matches("between|greater than", result.Error);
    }

    [Fact]
    public void Strength_range_message_says_greater_than_zero()
    {
        var result = ExtractedValueValidator.Validate("YieldStrengthPsi", "0", Today);

        Assert.Equal("Yield Strength (psi) must be greater than 0 and at most 1000000.", result.Error);
    }

    [Fact]
    public void Chemistry_range_message_is_readable()
    {
        var result = ExtractedValueValidator.Validate("CarbonPct", "101", Today);

        Assert.Equal("Carbon must be between 0 and 100.", result.Error);
    }

    // --- blank input: clear vs required ---

    [Theory]
    [InlineData("CarbonPct", "")]
    [InlineData("CarbonPct", "   ")]
    [InlineData("CarbonPct", null)]
    [InlineData("CertDate", "")]
    [InlineData("Specification", " ")]
    public void Blank_optional_input_means_clear(string key, string? input)
    {
        var result = ExtractedValueValidator.Validate(key, input, Today);

        Assert.True(result.IsValid);
        Assert.True(result.IsCleared);
        Assert.Null(result.Number);
        Assert.Null(result.Text);
        Assert.Null(result.Date);
    }

    [Theory]
    [InlineData("HeatNumber", "")]
    [InlineData("HeatNumber", "   ")]
    [InlineData("Grade", null)]
    public void Blank_required_input_is_rejected(string key, string? input)
    {
        var result = ExtractedValueValidator.Validate(key, input, Today);

        Assert.False(result.IsValid);
        Assert.Contains("is required", result.Error);
    }

    // --- text ---

    [Fact]
    public void Text_is_trimmed()
    {
        var result = ExtractedValueValidator.Validate("Grade", "  304L  ", Today);

        Assert.True(result.IsValid);
        Assert.Equal("304L", result.Text);
    }

    [Fact]
    public void Text_at_the_max_length_is_accepted_and_one_over_is_rejected()
    {
        var max = ExtractedValueValidator.RuleFor("HeatNumber").MaxLength;

        Assert.True(ExtractedValueValidator.Validate("HeatNumber", new string('A', max), Today).IsValid);

        var over = ExtractedValueValidator.Validate("HeatNumber", new string('A', max + 1), Today);
        Assert.False(over.IsValid);
        Assert.Contains($"at most {max} characters", over.Error);
    }

    // --- dates ---

    [Theory]
    [InlineData("2021-11-05")]
    [InlineData("1950-01-01")]                      // earliest allowed
    [InlineData("2026-11-26")]                      // today
    [InlineData("2026-11-27")]                      // +1 day tolerance (time zones)
    public void Valid_dates_are_accepted(string input)
    {
        var result = ExtractedValueValidator.Validate("CertDate", input, Today);

        Assert.True(result.IsValid, result.Error);
        Assert.Equal(DateOnly.ParseExact(input, "yyyy-MM-dd"), result.Date);
    }

    [Theory]
    [InlineData("11/05/2021")]
    [InlineData("2021-13-01")]
    [InlineData("2021-02-30")]
    [InlineData("05-11-2021")]
    [InlineData("Nov 5 2021")]
    [InlineData("2021-1-5")]
    public void Non_ISO_or_invalid_dates_are_rejected(string input)
    {
        var result = ExtractedValueValidator.Validate("CertDate", input, Today);

        Assert.False(result.IsValid);
        Assert.Contains("YYYY-MM-DD", result.Error);
    }

    [Theory]
    [InlineData("1949-12-31")]
    [InlineData("2026-11-28")]                      // 2 days ahead
    [InlineData("2099-01-01")]
    public void Out_of_range_dates_are_rejected(string input)
    {
        var result = ExtractedValueValidator.Validate("CertDate", input, Today);

        Assert.False(result.IsValid);
        Assert.Contains("between 1950-01-01 and today", result.Error);
    }

    // --- typed checks used by the store ---

    [Fact]
    public void Typed_number_check_flags_out_of_range_and_allows_null_for_optional_fields()
    {
        Assert.Null(ExtractedValueValidator.CheckNumber("CarbonPct", null));
        Assert.Null(ExtractedValueValidator.CheckNumber("CarbonPct", 0.014m));
        Assert.Equal("Carbon must be between 0 and 100.", ExtractedValueValidator.CheckNumber("CarbonPct", 150m));
    }

    [Fact]
    public void Typed_text_check_enforces_required_and_length()
    {
        Assert.Null(ExtractedValueValidator.CheckText("Specification", null));
        Assert.NotNull(ExtractedValueValidator.CheckText("HeatNumber", " "));
        Assert.NotNull(ExtractedValueValidator.CheckText("ReductionOfAreaPct", new string('9', 51)));
        Assert.Null(ExtractedValueValidator.CheckText("ReductionOfAreaPct", new string('9', 50)));
    }

    [Fact]
    public void Typed_date_check_enforces_the_range_and_allows_null()
    {
        Assert.Null(ExtractedValueValidator.CheckDate("CertDate", null, Today));
        Assert.Null(ExtractedValueValidator.CheckDate("CertDate", new DateOnly(2021, 11, 5), Today));
        Assert.NotNull(ExtractedValueValidator.CheckDate("CertDate", new DateOnly(1900, 1, 1), Today));
    }
}
