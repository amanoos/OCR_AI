using System.Text.RegularExpressions;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

/// <summary>
/// The prompt and the model are "two halves of the same contract": if a field is
/// added to the extraction model but not to the prompt, the model is never asked
/// for it. These tests fail when the halves drift apart.
/// </summary>
public class PromptContractTests
{
    private static string SnakeCase(string pascal) =>
        Regex.Replace(pascal, "(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();

    [Fact]
    public void Every_extractable_field_is_named_in_the_prompt()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        var missing = ExtractedValueValidator.Rules
            .Select(r => SnakeCase(r.Key))
            .Where(key => !prompt.Contains(key, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, "Fields the prompt never asks for: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("carbon_equivalent")]
    [InlineData("hardness_value")]
    [InlineData("hardness_scale")]
    [InlineData("nitrogen_pct")]
    [InlineData("cobalt_pct")]
    [InlineData("niobium_pct")]
    public void Snake_case_conversion_matches_the_json_keys_the_parser_reads(string expected)
    {
        var generated = ExtractedValueValidator.Rules.Select(r => SnakeCase(r.Key)).ToList();

        Assert.Contains(expected, generated);
    }

    [Fact]
    public void New_fields_are_marked_include_only_if_printed_while_the_original_rule_is_kept()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        // original fields: always return the key
        Assert.Contains("always include the key with", prompt);
        // new fields: leave out when absent
        Assert.Contains("ADDITIONAL FIELDS", prompt);
        Assert.Contains("do NOT return null placeholders", prompt);
    }

    [Fact]
    public void The_prompt_explains_ppm_conversion_and_trace_value_limits()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        Assert.Contains("ppm", prompt);
        Assert.Contains("dividing by 10,000", prompt);
        Assert.Contains("\"<0.001\"", prompt);
        Assert.Contains("ND", prompt);
    }

    [Fact]
    public void The_prompt_warns_that_CO_is_cobalt_not_carbon()
    {
        Assert.Contains("\"CO\" is cobalt", CertExtractionPrompts.SystemPrompt);
    }

    [Theory]
    [InlineData("\"6-8\"")]
    [InlineData("7.0:1")]
    [InlineData("EAF + AOD")]
    [InlineData("EAF+AOD")]
    [InlineData("6 pieces")]
    [InlineData("Round bar, annealed, cold finished")]
    [InlineData("304L")]
    [InlineData("P02V")]
    public void Prompt_examples_are_not_copied_from_the_certificate_we_test_with(string sampleValue)
    {
        // An example identical to a value on the test cert makes the model copy the example
        // instead of reading the document — and makes our own verification look better than it is.
        Assert.DoesNotContain(sampleValue, CertExtractionPrompts.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Quantity_must_come_from_a_pieces_column_never_a_mark_or_skid_number()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        Assert.Contains("Pieces, Pcs, Qty or Quantity", prompt);
        Assert.Contains("never from a Mark, Skid, Bundle", prompt);
    }

    [Fact]
    public void Every_catalog_measurement_key_is_offered_to_the_model()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        var missing = SteelCertExtraction.Domain.Models.MeasurementCatalog.Kinds
            .Select(k => k.Key)
            .Where(key => !prompt.Contains(key, StringComparison.Ordinal))
            .ToList();

        Assert.True(missing.Count == 0, "Catalog keys the prompt never offers: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_measurements_section_forbids_guessing_conversion_and_duplicates()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        Assert.Contains("MEASUREMENTS (optional; include ONLY what the document prints)", prompt);
        Assert.Contains("no unit conversion", prompt);
        Assert.Contains("Never guess", prompt);
        Assert.Contains("do not return any impact_ element", prompt);
        Assert.Contains("Do NOT repeat anything already covered", prompt);
        Assert.Contains("at most ONE element per key", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"other\"", prompt);
    }

    [Fact]
    public void Hardness_value_and_scale_are_requested_together_as_printed()
    {
        var prompt = CertExtractionPrompts.SystemPrompt;

        Assert.Contains("hardness_value and hardness_scale together, or neither", prompt);
        Assert.Contains("first one listed", prompt);
    }
}
