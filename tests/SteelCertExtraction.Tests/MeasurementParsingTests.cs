using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class MeasurementParsingTests
{
    private sealed class FakeRasterizer : IPdfRasterizer
    {
        public Task<List<RasterizedPage>> RasterizePagesAsync(Stream pdfStream, int dpi, CancellationToken ct) =>
            Task.FromResult(new List<RasterizedPage> { new(1, new byte[] { 1 }, 10, 10) });
    }

    private sealed class FakeLlm(string response) : ILlmChatClient
    {
        public string ModelIdentifier => "fake";
        public Task<string> SendMultimodalAsync(
            string systemPrompt, string userPrompt, List<byte[]> images, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    /// <summary>Wraps a "measurements" JSON array in a minimal certificate and extracts it.</summary>
    private static async Task<List<ExtractedMeasurement>> Parse(string measurementsJson)
    {
        var json = $$"""
            {"certificates":[{
              "heat_number":{"value":"H1","page":1},
              "grade":{"value":"A36","page":1},
              "measurements": {{measurementsJson}}
            }]}
            """;
        var result = await new CertExtractionService(new FakeRasterizer(), new FakeLlm(json))
            .ExtractAsync(new MemoryStream(), "x.pdf", vendorKey: null);
        return Assert.Single(result.Certificates).Measurements;
    }

    // --- the happy path ---

    [Fact]
    public async Task A_catalog_measurement_is_mapped_with_label_category_original_page_and_box()
    {
        var list = await Parse("""
            [{"key":"impact_energy","value":"54","unit":"J","condition":"-40 C, 10x10 mm",
              "page":2,"bbox":{"x":0.1,"y":0.2,"w":0.3,"h":0.04}}]
            """);

        var m = Assert.Single(list);
        Assert.Equal("impact_energy", m.Key);
        Assert.Equal("Impact energy", m.Label);
        Assert.Equal("Impact", m.Category);
        Assert.Equal(new MeasurementValue("54", "J", "-40 C, 10x10 mm"), m.Field.Value);
        Assert.Equal(m.Field.Value, m.Field.OriginalValue);
        Assert.False(m.Field.IsEdited);
        Assert.False(m.Field.Confirmed);
        Assert.Equal(2, m.Field.PageNumber);
        Assert.Equal(0.3, m.Field.BoundingBox!.W);
        Assert.Equal("Measurement:impact_energy", m.EvidenceName);
    }

    [Fact]
    public async Task Numbers_are_accepted_as_values_and_kept_as_text()
    {
        var list = await Parse("""[{"key":"impact_energy","value":54,"unit":"J"},{"key":"grain_size","value":6.5}]""");

        Assert.Equal("54", list.Single(m => m.Key == "impact_energy").Field.Value!.Value);
        Assert.Equal("6.5", list.Single(m => m.Key == "grain_size").Field.Value!.Value);
    }

    [Fact]
    public async Task Keys_are_case_and_whitespace_insensitive()
    {
        var list = await Parse("""[{"key":"  Grain_Size ","value":"6-8"}]""");

        Assert.Equal("grain_size", Assert.Single(list).Key);
    }

    [Fact]
    public async Task Blank_unit_and_condition_become_null_and_missing_page_is_zero()
    {
        var m = Assert.Single(await Parse("""[{"key":"grain_size","value":"6-8","unit":"","condition":"  "}]"""));

        Assert.Null(m.Field.Value!.Unit);
        Assert.Null(m.Field.Value.Condition);
        Assert.Equal(0, m.Field.PageNumber);
        Assert.Null(m.Field.BoundingBox);
    }

    [Fact]
    public async Task Long_values_are_kept_as_printed_not_silently_truncated()
    {
        var longText = new string('x', 300);

        var m = Assert.Single(await Parse($$"""[{"key":"heat_treatment","value":"{{longText}}"}]"""));

        Assert.Equal(300, m.Field.Value!.Value!.Length);   // the store will flag it; the reviewer decides
    }

    // --- "other" ---

    [Fact]
    public async Task An_unknown_key_becomes_other_with_the_printed_label()
    {
        var m = Assert.Single(await Parse("""[{"key":"other","label":"Delta ferrite","value":"5","unit":"%"}]"""));

        Assert.Equal("other", m.Key);
        Assert.Equal("Delta ferrite", m.Label);
        Assert.Equal("Other", m.Category);
        Assert.Equal("Measurement:other:Delta ferrite", m.EvidenceName);
    }

    [Fact]
    public async Task A_key_outside_the_catalog_is_kept_as_other_rather_than_dropped()
    {
        var m = Assert.Single(await Parse("""[{"key":"magnetic_permeability","value":"1.02"}]"""));

        Assert.Equal("other", m.Key);
        Assert.Equal("magnetic_permeability", m.Label);   // no printed label → the key the model used
    }

    [Fact]
    public async Task An_other_entry_with_no_label_and_no_usable_key_is_skipped()
    {
        Assert.Empty(await Parse("""[{"key":"other","value":"5"},{"value":"6"}]"""));
    }

    [Fact]
    public async Task Over_long_other_labels_are_cut_to_the_column_size()
    {
        var longLabel = new string('L', 150);

        var m = Assert.Single(await Parse($$"""[{"key":"other","label":"{{longLabel}}","value":"5"}]"""));

        Assert.Equal(MeasurementLimits.Label, m.Label.Length);
    }

    [Fact]
    public async Task At_most_ten_other_measurements_are_kept_but_catalog_ones_do_not_count()
    {
        var others = string.Join(",", Enumerable.Range(1, 15).Select(i => $$"""{"key":"other","label":"Thing {{i}}","value":"{{i}}"}"""));

        var list = await Parse($$"""[{{others}},{"key":"grain_size","value":"6"}]""");

        Assert.Equal(10, list.Count(m => m.Key == "other"));
        Assert.Contains(list, m => m.Key == "grain_size");
        Assert.DoesNotContain(list, m => m.Label == "Thing 11");
    }

    // --- one value per property ---

    [Fact]
    public async Task A_repeated_catalog_key_keeps_the_first_listed()
    {
        var list = await Parse("""[{"key":"impact_energy","value":"54"},{"key":"impact_energy","value":"60"}]""");

        Assert.Equal("54", Assert.Single(list).Field.Value!.Value);
    }

    [Fact]
    public async Task A_repeated_other_label_keeps_the_first_listed_ignoring_case()
    {
        var list = await Parse("""
            [{"key":"other","label":"Delta ferrite","value":"5"},
             {"key":"other","label":"delta FERRITE","value":"6"},
             {"key":"other","label":"Magnetic permeability","value":"1.02"}]
            """);

        Assert.Equal(2, list.Count);
        Assert.Equal("5", list.Single(m => m.Label == "Delta ferrite").Field.Value!.Value);
    }

    // --- tolerance: nothing here may fail the whole extraction ---

    [Fact]
    public async Task A_missing_measurements_property_gives_an_empty_list()
    {
        var json = """{"certificates":[{"heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1}}]}""";
        var result = await new CertExtractionService(new FakeRasterizer(), new FakeLlm(json))
            .ExtractAsync(new MemoryStream(), "x.pdf", null);

        Assert.Empty(Assert.Single(result.Certificates).Measurements);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("\"none\"")]
    [InlineData("5")]
    [InlineData("[]")]
    public async Task A_measurements_value_that_is_not_a_usable_array_is_ignored(string json)
    {
        Assert.Empty(await Parse(json));
    }

    [Fact]
    public async Task Entries_that_are_not_objects_or_have_no_value_are_skipped_and_the_rest_survive()
    {
        var list = await Parse("""
            [5, "text", null, [1,2],
             {"key":"impact_energy"},
             {"key":"impact_shear_area","value":null},
             {"key":"impact_shear_area","value":"  "},
             {"key":"grain_size","value":"6-8"}]
            """);

        Assert.Equal("grain_size", Assert.Single(list).Key);
    }

    [Fact]
    public async Task A_malformed_entry_is_skipped_without_failing_the_response_or_its_neighbours()
    {
        var list = await Parse("""
            [{"key":"impact_energy","value":"54","page":"not a number"},
             {"key":"grain_size","value":"6-8","page":1,"bbox":{"x":"bad"}},
             {"key":"product_form","value":"Round bar","page":1}]
            """);

        Assert.Equal("product_form", Assert.Single(list).Key);
    }

    [Fact]
    public async Task Odd_value_types_inside_an_entry_do_not_break_it()
    {
        var list = await Parse("""
            [{"key":"grain_size","value":"6-8","unit":{"a":1},"condition":[1,2],"label":true}]
            """);

        var m = Assert.Single(list);
        Assert.Equal("6-8", m.Field.Value!.Value);
        Assert.Null(m.Field.Value.Unit);
        Assert.Null(m.Field.Value.Condition);
    }

    [Fact]
    public async Task Measurements_and_the_fixed_fields_coexist()
    {
        var json = """
            {"certificates":[{
              "heat_number":{"value":"P02V","page":1},
              "grade":{"value":"304L","page":1},
              "chemistry":{"nitrogen_pct":{"value":0.074,"page":1}},
              "measurements":[{"key":"grain_size","value":"6-8","page":1}]
            }]}
            """;
        var result = await new CertExtractionService(new FakeRasterizer(), new FakeLlm(json))
            .ExtractAsync(new MemoryStream(), "x.pdf", null);

        var cert = Assert.Single(result.Certificates);
        Assert.Equal(0.074m, cert.Chemistry.NitrogenPct!.Value);
        Assert.Single(cert.Measurements);
    }
}
