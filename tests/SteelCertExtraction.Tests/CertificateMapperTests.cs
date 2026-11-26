using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

public class CertificateMapperTests
{
    private static ExtractedField<T> F<T>(T value, int page = 1, bool confirmed = false, BoundingBox? bbox = null) =>
        new() { Value = value, OriginalValue = value, PageNumber = page, Confirmed = confirmed, BoundingBox = bbox };

    private static ExtractionResult Extraction() => new()
    {
        DocumentId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        SourceFileName = "mtr.pdf",
        PageCount = 2,
        ExtractedAtUtc = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc),
        ModelUsed = "claude-test",
    };

    private static SteelCertificate FullCert() => new()
    {
        HeatNumber = F("  h123-a ", page: 1, confirmed: true, bbox: new BoundingBox { X = 0.1, Y = 0.2, W = 0.3, H = 0.05 }),
        Grade = F("304L", confirmed: true),
        Specification = F("ASTM A240"),
        CertifyingLab = F("Acme Lab"),
        CertDate = F<DateOnly?>(new DateOnly(2024, 3, 15)),
        PurchaseOrderNumber = F("PO-9"),
        Chemistry = new ChemistryComposition
        {
            CarbonPct = F<decimal?>(0.025m, page: 2, confirmed: true),
            ChromiumPct = F<decimal?>(18.1234m),
        },
        Mechanical = new MechanicalProperties
        {
            YieldStrengthPsi = F<decimal?>(42000.5m),
            ReductionOfAreaPct = F("55"),
        },
    };

    private static StoredCertificate Map(SteelCertificate cert) =>
        CertificateMapper.ToStored(cert, Extraction(), "/uploaded-certs/mtr.pdf",
            new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Maps_scalar_values_and_provenance()
    {
        var stored = Map(FullCert());

        Assert.Equal("304L", stored.Grade);
        Assert.Equal("ASTM A240", stored.Specification);
        Assert.Equal("Acme Lab", stored.CertifyingLab);
        Assert.Equal(new DateOnly(2024, 3, 15), stored.CertDate);
        Assert.Equal("PO-9", stored.PurchaseOrderNumber);
        Assert.Equal(0.025m, stored.CarbonPct);
        Assert.Equal(18.1234m, stored.ChromiumPct);
        Assert.Equal(42000.5m, stored.YieldStrengthPsi);
        Assert.Equal("55", stored.ReductionOfAreaPct);

        Assert.Equal("mtr.pdf", stored.SourceFileName);
        Assert.Equal("/uploaded-certs/mtr.pdf", stored.StoredPdfPath);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), stored.ExtractionDocumentId);
        Assert.Equal("claude-test", stored.ModelUsed);
        Assert.Equal(new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc), stored.ExtractedAtUtc);
        Assert.Equal(new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc), stored.ConfirmedAtUtc);
    }

    [Fact]
    public void Heat_number_is_normalized_but_original_text_kept_in_evidence()
    {
        var stored = Map(FullCert());

        Assert.Equal("H123-A", stored.HeatNumber);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "HeatNumber");
        Assert.Equal("  h123-a ", evidence.ExtractedValueText);
    }

    [Theory]
    [InlineData("  h1 ", "H1")]
    [InlineData("abc", "ABC")]
    [InlineData("H1", "H1")]
    public void NormalizeHeatNumber_trims_and_uppercases(string input, string expected) =>
        Assert.Equal(expected, CertificateMapper.NormalizeHeatNumber(input));

    [Fact]
    public void Missing_numeric_values_are_null_never_zero_and_get_no_evidence()
    {
        var cert = FullCert();
        cert.Chemistry.VanadiumPct = F<decimal?>(null, page: 0);   // model said null
        cert.Mechanical.TensileStrengthPsi = null;                  // field omitted

        var stored = Map(cert);

        Assert.Null(stored.VanadiumPct);
        Assert.Null(stored.TensileStrengthPsi);
        Assert.Null(stored.NickelPct); // never populated
        Assert.DoesNotContain(stored.Evidence, e => e.FieldName == "VanadiumPct");
        Assert.DoesNotContain(stored.Evidence, e => e.FieldName == "TensileStrengthPsi");
        Assert.DoesNotContain(stored.Evidence, e => e.FieldName == "NickelPct");
    }

    [Fact]
    public void Blank_string_values_get_no_evidence()
    {
        var cert = FullCert();
        cert.Specification = F("   ");

        var stored = Map(cert);

        Assert.DoesNotContain(stored.Evidence, e => e.FieldName == "Specification");
    }

    [Fact]
    public void Evidence_carries_page_bbox_and_confirmed_flag()
    {
        var stored = Map(FullCert());

        var heat = Assert.Single(stored.Evidence, e => e.FieldName == "HeatNumber");
        Assert.Equal(1, heat.Page);
        Assert.Equal(0.1, heat.BboxX);
        Assert.Equal(0.2, heat.BboxY);
        Assert.Equal(0.3, heat.BboxW);
        Assert.Equal(0.05, heat.BboxH);
        Assert.True(heat.Confirmed);

        var carbon = Assert.Single(stored.Evidence, e => e.FieldName == "CarbonPct");
        Assert.Equal(2, carbon.Page);
        Assert.Equal("0.025", carbon.ExtractedValueText);
        Assert.Null(carbon.BboxX); // model gave no bbox
        Assert.True(carbon.Confirmed);
    }

    [Fact]
    public void Unconfirmed_optional_values_are_stored_with_Confirmed_false()
    {
        var stored = Map(FullCert());

        var spec = Assert.Single(stored.Evidence, e => e.FieldName == "Specification");
        Assert.False(spec.Confirmed);
        var chromium = Assert.Single(stored.Evidence, e => e.FieldName == "ChromiumPct");
        Assert.False(chromium.Confirmed);
    }

    [Fact]
    public void Evidence_has_one_row_per_field_name()
    {
        var stored = Map(FullCert());

        var names = stored.Evidence.Select(e => e.FieldName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Decimal_and_date_evidence_text_is_culture_invariant()
    {
        var stored = Map(FullCert());

        Assert.Equal("18.1234", stored.Evidence.Single(e => e.FieldName == "ChromiumPct").ExtractedValueText);
        Assert.Equal("2024-03-15", stored.Evidence.Single(e => e.FieldName == "CertDate").ExtractedValueText);
    }

    // --- edited / added / cleared values ---

    [Fact]
    public void Unedited_fields_are_not_marked_edited()
    {
        var stored = Map(FullCert());

        Assert.NotEmpty(stored.Evidence);
        Assert.All(stored.Evidence, e => Assert.False(e.Edited));
    }

    [Fact]
    public void Edited_number_stores_the_final_value_in_the_column_and_the_original_in_evidence()
    {
        var cert = FullCert();
        cert.Chemistry.CarbonPct!.Value = 0.02m;   // model said 0.025

        var stored = Map(cert);

        Assert.Equal(0.02m, stored.CarbonPct);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "CarbonPct");
        Assert.Equal("0.025", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
        Assert.Equal(2, evidence.Page);              // provenance of the original is kept
    }

    [Fact]
    public void Added_value_has_no_original_and_is_marked_edited()
    {
        var cert = FullCert();
        cert.Chemistry.VanadiumPct = new ExtractedField<decimal?> { Value = 0.05m, OriginalValue = null, PageNumber = 0 };

        var stored = Map(cert);

        Assert.Equal(0.05m, stored.VanadiumPct);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "VanadiumPct");
        Assert.Null(evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
        Assert.Equal(0, evidence.Page);
        Assert.Null(evidence.BboxX);
        Assert.False(evidence.Confirmed);            // an added value is unconfirmed until ticked
    }

    [Fact]
    public void Cleared_value_is_null_in_the_column_but_leaves_an_evidence_row()
    {
        var cert = FullCert();
        cert.Chemistry.ChromiumPct!.Value = null;    // model said 18.1234

        var stored = Map(cert);

        Assert.Null(stored.ChromiumPct);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "ChromiumPct");
        Assert.Equal("18.1234", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
    }

    [Fact]
    public void Edited_heat_number_is_normalized_in_the_column_and_the_original_text_is_kept()
    {
        var cert = FullCert();
        cert.HeatNumber!.Value = " p02v ";            // model said "  h123-a "

        var stored = Map(cert);

        Assert.Equal("P02V", stored.HeatNumber);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "HeatNumber");
        Assert.Equal("  h123-a ", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
    }

    [Fact]
    public void Edited_text_and_date_keep_their_originals()
    {
        var cert = FullCert();
        cert.Specification!.Value = "ASTM A276";
        cert.CertDate!.Value = new DateOnly(2024, 3, 16);

        var stored = Map(cert);

        Assert.Equal("ASTM A276", stored.Specification);
        Assert.Equal(new DateOnly(2024, 3, 16), stored.CertDate);
        Assert.Equal("ASTM A240", stored.Evidence.Single(e => e.FieldName == "Specification").ExtractedValueText);
        Assert.Equal("2024-03-15", stored.Evidence.Single(e => e.FieldName == "CertDate").ExtractedValueText);
        Assert.True(stored.Evidence.Single(e => e.FieldName == "CertDate").Edited);
    }

    [Fact]
    public void Cleared_date_is_null_with_an_evidence_row()
    {
        var cert = FullCert();
        cert.CertDate!.Value = null;

        var stored = Map(cert);

        Assert.Null(stored.CertDate);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "CertDate");
        Assert.Equal("2024-03-15", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
    }

    [Fact]
    public void Whitespace_only_difference_in_text_is_not_an_edit()
    {
        var cert = FullCert();
        cert.Specification!.Value = " ASTM A240 ";

        var stored = Map(cert);

        Assert.False(stored.Evidence.Single(e => e.FieldName == "Specification").Edited);
    }

    [Fact]
    public void Edit_does_not_change_the_confirmed_flag_it_passes_through()
    {
        var cert = FullCert();
        cert.Chemistry.CarbonPct!.Value = 0.02m;
        cert.Chemistry.CarbonPct.Confirmed = false;   // the UI un-ticks on edit
        cert.Chemistry.ChromiumPct!.Value = 18m;
        cert.Chemistry.ChromiumPct.Confirmed = true;  // ...and the reviewer ticked it again

        var stored = Map(cert);

        Assert.False(stored.Evidence.Single(e => e.FieldName == "CarbonPct").Confirmed);
        Assert.True(stored.Evidence.Single(e => e.FieldName == "ChromiumPct").Confirmed);
    }

    [Fact]
    public void A_field_the_model_left_empty_and_nobody_touched_still_leaves_no_row()
    {
        var cert = FullCert();
        cert.Chemistry.NickelPct = new ExtractedField<decimal?> { Value = null, OriginalValue = null, PageNumber = 0 };

        var stored = Map(cert);

        Assert.DoesNotContain(stored.Evidence, e => e.FieldName == "NickelPct");
    }
}
