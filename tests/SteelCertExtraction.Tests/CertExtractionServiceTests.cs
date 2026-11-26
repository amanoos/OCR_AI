using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public class CertExtractionServiceTests
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

    private static Task<SteelCertExtraction.Domain.Models.ExtractionResult> Extract(string llmJson) =>
        new CertExtractionService(new FakeRasterizer(), new FakeLlm(llmJson))
            .ExtractAsync(new MemoryStream(), "x.pdf", vendorKey: null);

    [Fact]
    public async Task Null_value_page_and_bbox_for_missing_numeric_field_is_mapped_as_missing()
    {
        // Regression: vanadium_pct with "value": null crashed System.Text.Json
        // because the numeric value type couldn't hold null.
        var result = await Extract("""
            {"certificates":[{
              "heat_number":{"value":"H123","page":1,"bbox":{"x":0.1,"y":0.2,"w":0.3,"h":0.05}},
              "grade":{"value":"304L","page":1,"bbox":null},
              "chemistry":{
                "carbon_pct":{"value":0.025,"page":1,"bbox":{"x":0.1,"y":0.4,"w":0.1,"h":0.03}},
                "vanadium_pct":{"value":null,"page":null,"bbox":null}
              },
              "mechanical":{"yield_strength_psi":{"value":null}}
            }]}
            """);

        var cert = Assert.Single(result.Certificates);
        Assert.Equal("H123", cert.HeatNumber!.Value);
        Assert.Equal(0.025m, cert.Chemistry.CarbonPct!.Value);

        // Missing values must be null, never silently 0.
        Assert.Null(cert.Chemistry.VanadiumPct!.Value);
        Assert.Equal(0, cert.Chemistry.VanadiumPct.PageNumber);
        Assert.Null(cert.Chemistry.VanadiumPct.BoundingBox);
        Assert.Null(cert.Mechanical.YieldStrengthPsi!.Value);
        Assert.Null(cert.Chemistry.ManganesePct); // field omitted entirely
        Assert.Empty(cert.MissingRequiredFields);
    }

    [Fact]
    public async Task Missing_heat_number_is_flagged_as_missing_required()
    {
        var result = await Extract("""
            {"certificates":[{"heat_number":{"value":null,"page":null},"grade":{"value":"A36","page":1}}]}
            """);

        var cert = Assert.Single(result.Certificates);
        Assert.Contains("HeatNumber", cert.MissingRequiredFields);
        Assert.DoesNotContain("Grade", cert.MissingRequiredFields);
    }

    [Fact]
    public async Task Markdown_fenced_json_is_accepted()
    {
        var result = await Extract("```json\n{\"certificates\":[]}\n```");
        Assert.Empty(result.Certificates);
    }

    [Fact]
    public async Task Invalid_json_throws_parse_exception_with_raw_response()
    {
        var ex = await Assert.ThrowsAsync<CertExtractionParseException>(() => Extract("not json"));
        Assert.Equal("not json", ex.RawResponse);
    }

    [Fact]
    public async Task Mapped_fields_remember_what_the_model_said_and_are_not_edited()
    {
        var result = await Extract("""
            {"certificates":[{
              "heat_number":{"value":"P02V","page":1},
              "grade":{"value":"304L","page":1},
              "cert_date":{"value":"2021-11-05","page":1},
              "chemistry":{"carbon_pct":{"value":0.014,"page":1}},
              "mechanical":{"reduction_of_area_pct":{"value":"77.11","page":1}}
            }]}
            """);

        var cert = Assert.Single(result.Certificates);
        Assert.Equal("P02V", cert.HeatNumber!.OriginalValue);
        Assert.Equal("P02V", cert.HeatNumber.Value);
        Assert.Equal(0.014m, cert.Chemistry.CarbonPct!.OriginalValue);
        Assert.Equal(new DateOnly(2021, 11, 5), cert.CertDate!.Value);
        Assert.Equal(new DateOnly(2021, 11, 5), cert.CertDate.OriginalValue);
        Assert.Equal("77.11", cert.Mechanical.ReductionOfAreaPct!.OriginalValue);

        Assert.False(cert.HeatNumber.IsEdited);
        Assert.False(cert.Grade!.IsEdited);
        Assert.False(cert.CertDate.IsEdited);
        Assert.False(cert.Chemistry.CarbonPct.IsEdited);
        Assert.False(cert.Mechanical.ReductionOfAreaPct.IsEdited);
    }

    [Fact]
    public async Task A_model_null_value_has_a_null_original_and_is_not_edited()
    {
        var result = await Extract("""
            {"certificates":[{"heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1},
              "chemistry":{"vanadium_pct":{"value":null,"page":null,"bbox":null}}}]}
            """);

        var vanadium = Assert.Single(result.Certificates).Chemistry.VanadiumPct!;

        Assert.Null(vanadium.Value);
        Assert.Null(vanadium.OriginalValue);
        Assert.False(vanadium.IsEdited);
    }

    [Fact]
    public async Task An_unparseable_model_date_maps_to_a_missing_date()
    {
        var result = await Extract("""
            {"certificates":[{"heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1},
              "cert_date":{"value":"not a date","page":1}}]}
            """);

        Assert.Null(Assert.Single(result.Certificates).CertDate);
    }

    // --- more properties (typed) ---

    [Fact]
    public async Task New_elements_and_hardness_are_parsed_with_originals_pages_and_boxes()
    {
        var result = await Extract("""
            {"certificates":[{
              "heat_number":{"value":"P02V","page":1},
              "grade":{"value":"304L","page":1},
              "chemistry":{
                "nitrogen_pct":{"value":0.074,"page":1,"bbox":{"x":0.4,"y":0.6,"w":0.05,"h":0.02}},
                "cobalt_pct":{"value":0.29,"page":1},
                "carbon_equivalent":{"value":0.42,"page":1}
              },
              "mechanical":{
                "hardness_value":{"value":163,"page":1,"bbox":{"x":0.1,"y":0.7,"w":0.04,"h":0.02}},
                "hardness_scale":{"value":"HB","page":1}
              }
            }]}
            """);

        var cert = Assert.Single(result.Certificates);
        var nitrogen = cert.Chemistry.NitrogenPct!;
        Assert.Equal(0.074m, nitrogen.Value);
        Assert.Equal(0.074m, nitrogen.OriginalValue);
        Assert.Equal(1, nitrogen.PageNumber);
        Assert.Equal(0.4, nitrogen.BoundingBox!.X);
        Assert.False(nitrogen.IsEdited);

        Assert.Equal(0.29m, cert.Chemistry.CobaltPct!.Value);
        Assert.Equal(0.42m, cert.Chemistry.CarbonEquivalent!.Value);
        Assert.Equal(163m, cert.Mechanical.HardnessValue!.Value);
        Assert.Equal("HB", cert.Mechanical.HardnessScale!.Value);
        Assert.Equal("HB", cert.Mechanical.HardnessScale.OriginalValue);
        Assert.False(cert.Mechanical.HardnessValue.IsEdited);
    }

    [Fact]
    public async Task New_fields_the_model_omitted_are_null_not_zero()
    {
        var result = await Extract("""
            {"certificates":[{"heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1}}]}
            """);

        var cert = Assert.Single(result.Certificates);
        Assert.Null(cert.Chemistry.NitrogenPct);
        Assert.Null(cert.Chemistry.BoronPct);
        Assert.Null(cert.Chemistry.CarbonEquivalent);
        Assert.Null(cert.Mechanical.HardnessValue);
        Assert.Null(cert.Mechanical.HardnessScale);
    }

    [Fact]
    public async Task Trace_values_like_less_than_and_ND_do_not_fail_the_whole_extraction()
    {
        // Before the lenient reader, ONE non-number made the whole response unparseable.
        var result = await Extract("""
            {"certificates":[{
              "heat_number":{"value":"H1","page":1},
              "grade":{"value":"A36","page":1},
              "chemistry":{
                "carbon_pct":{"value":"<0.01","page":1,"bbox":{"x":0.1,"y":0.1,"w":0.1,"h":0.1}},
                "oxygen_pct":{"value":"ND","page":1},
                "manganese_pct":{"value":"1.51","page":1},
                "nickel_pct":{"value":8.57,"page":1}
              }
            }]}
            """);

        var cert = Assert.Single(result.Certificates);
        Assert.Equal("H1", cert.HeatNumber!.Value);

        // The unparseable ones are "not found" (null), keeping their page/box for the reviewer...
        Assert.Null(cert.Chemistry.CarbonPct!.Value);
        Assert.Null(cert.Chemistry.CarbonPct.OriginalValue);
        Assert.NotNull(cert.Chemistry.CarbonPct.BoundingBox);
        Assert.Null(cert.Chemistry.OxygenPct!.Value);
        Assert.False(cert.Chemistry.CarbonPct.IsEdited);

        // ...and the rest are untouched, including a numeric string.
        Assert.Equal(1.51m, cert.Chemistry.ManganesePct!.Value);
        Assert.Equal(8.57m, cert.Chemistry.NickelPct!.Value);
    }
}
