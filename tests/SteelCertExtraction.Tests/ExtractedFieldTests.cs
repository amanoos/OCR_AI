using SteelCertExtraction.Domain.Models;

namespace SteelCertExtraction.Tests;

public class ExtractedFieldTests
{
    [Fact]
    public void A_new_empty_field_is_not_edited()
    {
        Assert.False(new ExtractedField<decimal?>().IsEdited);
        Assert.False(new ExtractedField<string>().IsEdited);
        Assert.False(new ExtractedField<DateOnly?>().IsEdited);
    }

    [Fact]
    public void A_field_whose_value_equals_the_original_is_not_edited()
    {
        var field = new ExtractedField<decimal?> { Value = 0.014m, OriginalValue = 0.014m };

        Assert.False(field.IsEdited);
    }

    [Fact]
    public void Changing_a_number_makes_it_edited_and_setting_it_back_clears_it()
    {
        var field = new ExtractedField<decimal?> { Value = 0.014m, OriginalValue = 0.014m };

        field.Value = 0.015m;
        Assert.True(field.IsEdited);

        field.Value = field.OriginalValue;
        Assert.False(field.IsEdited);
    }

    [Fact]
    public void Decimal_scale_does_not_count_as_an_edit()
    {
        var field = new ExtractedField<decimal?> { Value = 1.50m, OriginalValue = 1.5m };

        Assert.False(field.IsEdited);
    }

    [Fact]
    public void Adding_a_value_the_model_missed_is_an_edit()
    {
        var field = new ExtractedField<decimal?> { Value = 0.02m, OriginalValue = null };

        Assert.True(field.IsEdited);
    }

    [Fact]
    public void Clearing_a_value_the_model_extracted_is_an_edit()
    {
        var field = new ExtractedField<decimal?> { Value = null, OriginalValue = 0.02m };

        Assert.True(field.IsEdited);
    }

    [Fact]
    public void Text_is_compared_trimmed()
    {
        var field = new ExtractedField<string> { Value = "  304L ", OriginalValue = "304L" };

        Assert.False(field.IsEdited);

        field.Value = "304";
        Assert.True(field.IsEdited);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Null_and_blank_text_are_the_same_no_value(string? value, string? original)
    {
        var field = new ExtractedField<string> { Value = value, OriginalValue = original };

        Assert.False(field.IsEdited);
    }

    [Fact]
    public void Text_added_or_cleared_is_an_edit()
    {
        Assert.True(new ExtractedField<string> { Value = "NAS", OriginalValue = null }.IsEdited);
        Assert.True(new ExtractedField<string> { Value = null, OriginalValue = "NAS" }.IsEdited);
    }

    [Fact]
    public void A_date_can_be_changed_cleared_and_restored()
    {
        var field = new ExtractedField<DateOnly?> { Value = new DateOnly(2021, 11, 5), OriginalValue = new DateOnly(2021, 11, 5) };
        Assert.False(field.IsEdited);

        field.Value = new DateOnly(2021, 11, 6);
        Assert.True(field.IsEdited);

        field.Value = null; // cleared
        Assert.True(field.IsEdited);

        field.Value = new DateOnly(2021, 11, 5); // restored
        Assert.False(field.IsEdited);
    }
}
