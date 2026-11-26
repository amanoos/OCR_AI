using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

public sealed class ExtractionReportTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }
    }

    private sealed class FakeRasterizer : IPdfRasterizer
    {
        public Task<List<RasterizedPage>> RasterizePagesAsync(Stream pdfStream, int dpi, CancellationToken ct) =>
            Task.FromResult(new List<RasterizedPage> { new(1, new byte[] { 1 }, 10, 10) });
    }

    private sealed class FakeLlm(Func<string> response) : ILlmChatClient
    {
        public string ModelIdentifier => "fake-model";
        public Task<string> SendMultimodalAsync(
            string systemPrompt, string userPrompt, List<byte[]> images, CancellationToken ct = default) =>
            Task.FromResult(response());
    }

    private string TempFile(string extension = ".pdf")
    {
        var path = Path.Combine(Path.GetTempPath(), $"extract-report-test-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        _tempFiles.Add(path);
        return path;
    }

    private static LlmOptions Options(string? key = null) => new()
    {
        Provider = LlmProvider.Anthropic,
        Anthropic = { ApiKey = key },
    };

    private static async Task<(int Code, string Output)> Run(
        Func<string> llmResponse, string? path, bool showRaw = false, LlmOptions? options = null)
    {
        var writer = new StringWriter();
        var code = await ExtractionReport.RunAsync(
            new FakeLlm(llmResponse), new FakeRasterizer(), options ?? Options(), path, showRaw, writer);
        return (code, writer.ToString());
    }

    private const string SampleJson = """
        {"certificates":[{
          "heat_number":{"value":"P02V","page":1,"bbox":{"x":0.1,"y":0.2,"w":0.1,"h":0.05}},
          "grade":{"value":"304/304L","page":1},
          "cert_date":{"value":"2021-11-05","page":1,"bbox":{"x":0.7,"y":0.1,"w":0.1,"h":0.03}},
          "specification":{"value":"ASTM A276/17, ASTM A479/20, ASTM A182/20 CHEM ONLY, ASME SA479/19, ASME SA182/19 CHEM ONLY","page":1},
          "chemistry":{
            "carbon_pct":{"value":0.014,"page":1,"bbox":{"x":0.2,"y":0.5,"w":0.03,"h":0.02}},
            "nitrogen_pct":{"value":0.074,"page":1,"bbox":{"x":0.4,"y":0.6,"w":0.05,"h":0.02}},
            "cobalt_pct":{"value":0.29,"page":1},
            "oxygen_pct":{"value":"<0.001","page":1}
          },
          "mechanical":{
            "hardness_value":{"value":163,"page":1,"bbox":{"x":0.1,"y":0.7,"w":0.04,"h":0.02}},
            "hardness_scale":{"value":"HB","page":1}
          }
        }]}
        """;

    // --- success output ---

    [Fact]
    public async Task Success_exits_0_and_lists_found_fields_with_value_page_and_box()
    {
        var (code, output) = await Run(() => SampleJson, TempFile());

        Assert.Equal(0, code);
        Assert.Contains("Certificate 1 — heat P02V", output);
        Assert.Contains("Model    : fake-model", output);
        Assert.Matches(@"Nitrogen\s+0\.074\s+p1 box", output);
        Assert.Matches(@"Cobalt\s+0\.29\s+p1 no box", output);
        Assert.Matches(@"Hardness\s+163\s+p1 box", output);
        Assert.Matches(@"Hardness Scale\s+HB\s+p1 no box", output);
        Assert.Matches(@"Cert Date\s+2021-11-05\s+p1 box", output);
    }

    [Fact]
    public async Task Fields_not_found_show_a_dash_and_the_summary_counts_them()
    {
        var (_, output) = await Run(() => SampleJson, TempFile());

        Assert.Matches(@"Vanadium\s+—", output);
        // heat, grade, date, spec + carbon, nitrogen, cobalt + hardness, scale = 9 values found
        Assert.Contains($"Fields with a value: 9 of {ExtractedValueValidator.Rules.Count}", output);
    }

    [Fact]
    public async Task A_non_numeric_trace_value_is_shown_as_not_found_and_does_not_fail_the_run()
    {
        var (code, output) = await Run(() => SampleJson, TempFile());

        Assert.Equal(0, code);
        Assert.Matches(@"Oxygen\s+—", output);   // "<0.001" in the response
    }

    [Fact]
    public async Task Long_text_is_truncated_in_the_report()
    {
        var (_, output) = await Run(() => SampleJson, TempFile());

        var specLine = output.Split('\n').Single(l => l.Contains("Specification"));
        Assert.Contains("…", specLine);
        Assert.DoesNotContain("ASME SA182/19 CHEM ONLY", specLine);
    }

    [Fact]
    public async Task Response_size_is_reported()
    {
        var (_, output) = await Run(() => SampleJson, TempFile());

        Assert.Contains($"Response : {SampleJson.Length} characters", output);
    }

    [Fact]
    public async Task Raw_response_is_only_printed_with_show_raw()
    {
        var (_, without) = await Run(() => SampleJson, TempFile(), showRaw: false);
        var (_, with) = await Run(() => SampleJson, TempFile(), showRaw: true);

        Assert.DoesNotContain("--- raw model response ---", without);
        Assert.Contains("--- raw model response ---", with);
        Assert.Contains("\"nitrogen_pct\"", with);
    }

    [Fact]
    public async Task Multiple_certificates_are_numbered()
    {
        var (_, output) = await Run(() => """
            {"certificates":[
              {"heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1}},
              {"heat_number":{"value":"H2","page":2},"grade":{"value":"A36","page":2}}]}
            """, TempFile());

        Assert.Contains("Certificates found: 2", output);
        Assert.Contains("Certificate 1 — heat H1", output);
        Assert.Contains("Certificate 2 — heat H2", output);
    }

    // --- argument / file problems ---

    [Fact]
    public async Task Missing_path_prints_usage_and_exits_2()
    {
        var (code, output) = await Run(() => SampleJson, path: null);

        Assert.Equal(2, code);
        Assert.Contains("Usage:", output);
        Assert.Contains("--extract-file", output);
    }

    [Fact]
    public async Task Unknown_file_exits_1()
    {
        var (code, output) = await Run(() => SampleJson, Path.Combine(Path.GetTempPath(), "definitely-not-here.pdf"));

        Assert.Equal(1, code);
        Assert.Contains("File not found", output);
    }

    [Fact]
    public async Task Non_pdf_files_are_refused()
    {
        var (code, output) = await Run(() => SampleJson, TempFile(".txt"));

        Assert.Equal(1, code);
        Assert.Contains("Only PDF files", output);
    }

    // --- failures ---

    [Fact]
    public async Task Unparseable_model_output_exits_1_and_shows_the_raw_response()
    {
        var (code, output) = await Run(() => "this is not json", TempFile());

        Assert.Equal(1, code);
        Assert.Contains("could not be parsed", output);
        Assert.Contains("this is not json", output);
    }

    [Fact]
    public async Task Llm_failure_exits_1_and_masks_the_api_key()
    {
        var (code, output) = await Run(
            () => throw new LlmCallException("Anthropic call failed with Unauthorized: key sk-ant-secret-1"),
            TempFile(), options: Options("sk-ant-secret-1"));

        Assert.Equal(1, code);
        Assert.Contains("FAILED", output);
        Assert.Contains("Unauthorized", output);
        Assert.DoesNotContain("sk-ant-secret-1", output);
        Assert.Contains("***", output);
    }

    [Fact]
    public async Task The_api_key_is_masked_in_the_raw_response_too()
    {
        var (_, output) = await Run(
            () => SampleJson.Replace("P02V", "sk-ant-secret-2"), TempFile(), showRaw: true, options: Options("sk-ant-secret-2"));

        Assert.DoesNotContain("sk-ant-secret-2", output);
    }

    // --- measurements ---

    [Fact]
    public async Task Measurements_are_listed_with_value_unit_condition_category_page_and_box()
    {
        var (_, output) = await Run(() => """
            {"certificates":[{
              "heat_number":{"value":"H1","page":1},"grade":{"value":"A36","page":1},
              "measurements":[
                {"key":"grain_size","value":"6-8","page":1,"bbox":{"x":0.1,"y":0.2,"w":0.1,"h":0.02}},
                {"key":"impact_energy","value":"54","unit":"J","condition":"-40 C","page":2},
                {"key":"other","label":"Delta ferrite","value":"5","unit":"%","page":1}
              ]}]}
            """, TempFile());

        Assert.Contains("[Measurements] (3)", output);
        Assert.Matches(@"Grain size\s+6-8\s+\[HeatTreatment\] p1 box", output);
        Assert.Matches(@"Impact energy\s+54 J \(-40 C\)\s+\[Impact\] p2 no box", output);
        Assert.Matches(@"Delta ferrite\s+5 %\s+\[Other\] p1 no box", output);
    }

    [Fact]
    public async Task A_certificate_without_measurements_says_none()
    {
        var (_, output) = await Run(() => SampleJson, TempFile());

        Assert.Contains("[Measurements] (0)", output);
        Assert.Contains("(none)", output);
    }

    // --- Format directly ---

    [Fact]
    public void Format_handles_a_result_with_no_certificates()
    {
        var text = ExtractionReport.Format(new ExtractionResult { SourceFileName = "x.pdf", PageCount = 3, ModelUsed = "m" });

        Assert.Contains("Certificates found: 0", text);
        Assert.DoesNotContain("Certificate 1", text);
    }
}
