using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

public class CertificateMapperMorePropertiesTests
{
    private static ExtractedField<T> F<T>(T value, int page = 1, bool confirmed = false, BoundingBox? bbox = null) =>
        new() { Value = value, OriginalValue = value, PageNumber = page, Confirmed = confirmed, BoundingBox = bbox };

    private static ExtractionResult Extraction() => new()
    {
        DocumentId = Guid.NewGuid(),
        SourceFileName = "mtr.pdf",
        PageCount = 2,
        ExtractedAtUtc = DateTime.UtcNow,
        ModelUsed = "claude-test",
    };

    private static ExtractedMeasurement Measurement(
        string key, string label, string category, MeasurementValue? value, MeasurementValue? original = null, int page = 1,
        bool confirmed = false, BoundingBox? bbox = null) => new()
    {
        Key = key,
        Label = label,
        Category = category,
        Field = new ExtractedField<MeasurementValue>
        {
            Value = value, OriginalValue = original, PageNumber = page, Confirmed = confirmed, BoundingBox = bbox,
        },
    };

    private static SteelCertificate Cert() => new()
    {
        HeatNumber = F("H1", confirmed: true),
        Grade = F("304L", confirmed: true),
    };

    private static StoredCertificate Map(SteelCertificate cert) =>
        CertificateMapper.ToStored(cert, Extraction(), "/p.pdf", DateTime.UtcNow);

    // --- new typed fields ---

    [Fact]
    public void New_elements_and_hardness_map_to_their_columns_with_evidence()
    {
        var cert = Cert();
        cert.Chemistry.NitrogenPct = F<decimal?>(0.074m, bbox: new BoundingBox { X = 0.6, Y = 0.5, W = 0.03, H = 0.01 });
        cert.Chemistry.CobaltPct = F<decimal?>(0.29m, confirmed: true);
        cert.Chemistry.CarbonEquivalent = F<decimal?>(0.42m);
        cert.Mechanical.HardnessValue = F<decimal?>(163m);
        cert.Mechanical.HardnessScale = F("HB");

        var stored = Map(cert);

        Assert.Equal(0.074m, stored.NitrogenPct);
        Assert.Equal(0.29m, stored.CobaltPct);
        Assert.Equal(0.42m, stored.CarbonEquivalent);
        Assert.Equal(163m, stored.HardnessValue);
        Assert.Equal("HB", stored.HardnessScale);

        var nitrogen = Assert.Single(stored.Evidence, e => e.FieldName == "NitrogenPct");
        Assert.Equal("0.074", nitrogen.ExtractedValueText);
        Assert.Equal(0.6, nitrogen.BboxX);
        Assert.False(nitrogen.Edited);
        Assert.False(nitrogen.Confirmed);
        Assert.True(Assert.Single(stored.Evidence, e => e.FieldName == "CobaltPct").Confirmed);
        Assert.Equal("HB", Assert.Single(stored.Evidence, e => e.FieldName == "HardnessScale").ExtractedValueText);
    }

    [Fact]
    public void Every_new_element_column_is_mapped_and_missing_ones_stay_null_never_zero()
    {
        var cert = Cert();
        var c = cert.Chemistry;
        c.NitrogenPct = F<decimal?>(1m);
        c.CobaltPct = F<decimal?>(2m);
        c.BoronPct = F<decimal?>(3m);
        c.AluminumPct = F<decimal?>(4m);
        c.TitaniumPct = F<decimal?>(5m);
        c.NiobiumPct = F<decimal?>(6m);
        c.TungstenPct = F<decimal?>(7m);
        c.TinPct = F<decimal?>(8m);
        c.LeadPct = F<decimal?>(9m);
        c.ArsenicPct = F<decimal?>(10m);
        c.AntimonyPct = F<decimal?>(11m);
        c.CalciumPct = F<decimal?>(12m);
        c.OxygenPct = F<decimal?>(13m);
        c.HydrogenPct = F<decimal?>(14m);
        c.TantalumPct = F<decimal?>(15m);
        c.CarbonEquivalent = F<decimal?>(16m);

        var stored = Map(cert);

        Assert.Equal(
            new decimal?[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 },
            new[]
            {
                stored.NitrogenPct, stored.CobaltPct, stored.BoronPct, stored.AluminumPct, stored.TitaniumPct,
                stored.NiobiumPct, stored.TungstenPct, stored.TinPct, stored.LeadPct, stored.ArsenicPct,
                stored.AntimonyPct, stored.CalciumPct, stored.OxygenPct, stored.HydrogenPct, stored.TantalumPct,
                stored.CarbonEquivalent,
            });

        var empty = Map(Cert());
        Assert.Null(empty.NitrogenPct);
        Assert.Null(empty.HardnessValue);
        Assert.Null(empty.HardnessScale);
        Assert.DoesNotContain(empty.Evidence, e => e.FieldName is "NitrogenPct" or "HardnessValue" or "HardnessScale");
    }

    [Fact]
    public void New_typed_fields_follow_the_same_edit_add_clear_rules_as_the_old_ones()
    {
        var cert = Cert();
        cert.Chemistry.CobaltPct = new ExtractedField<decimal?> { Value = 0.30m, OriginalValue = 0.29m, PageNumber = 1 };   // edited
        cert.Chemistry.BoronPct = new ExtractedField<decimal?> { Value = 0.001m, OriginalValue = null };                    // added
        cert.Chemistry.NitrogenPct = new ExtractedField<decimal?> { Value = null, OriginalValue = 0.074m, PageNumber = 1 }; // cleared
        cert.Mechanical.HardnessScale = new ExtractedField<string> { Value = "HRC", OriginalValue = "HB", PageNumber = 1 };

        var stored = Map(cert);

        Assert.Equal(0.30m, stored.CobaltPct);
        Assert.Equal("0.29", stored.Evidence.Single(e => e.FieldName == "CobaltPct").ExtractedValueText);
        Assert.True(stored.Evidence.Single(e => e.FieldName == "CobaltPct").Edited);

        Assert.Equal(0.001m, stored.BoronPct);
        Assert.Null(stored.Evidence.Single(e => e.FieldName == "BoronPct").ExtractedValueText);
        Assert.True(stored.Evidence.Single(e => e.FieldName == "BoronPct").Edited);

        Assert.Null(stored.NitrogenPct);
        Assert.Equal("0.074", stored.Evidence.Single(e => e.FieldName == "NitrogenPct").ExtractedValueText);
        Assert.True(stored.Evidence.Single(e => e.FieldName == "NitrogenPct").Edited);

        Assert.Equal("HRC", stored.HardnessScale);
        Assert.Equal("HB", stored.Evidence.Single(e => e.FieldName == "HardnessScale").ExtractedValueText);
    }

    // --- measurements ---

    [Fact]
    public void An_unedited_measurement_becomes_a_row_plus_evidence_with_its_original_text()
    {
        var cert = Cert();
        var value = new MeasurementValue("54", "J", "-40 C, 10x10 mm");
        cert.Measurements.Add(Measurement("impact_energy", "Impact energy", "Impact", value, value, page: 2, confirmed: true,
            bbox: new BoundingBox { X = 0.1, Y = 0.2, W = 0.3, H = 0.04 }));

        var stored = Map(cert);

        var row = Assert.Single(stored.Measurements);
        Assert.Equal("impact_energy", row.Key);
        Assert.Equal("Impact energy", row.Label);
        Assert.Equal("Impact", row.Category);
        Assert.Equal("54", row.Value);
        Assert.Equal("J", row.Unit);
        Assert.Equal("-40 C, 10x10 mm", row.Condition);

        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:impact_energy");
        Assert.Equal("54 J (-40 C, 10x10 mm)", evidence.ExtractedValueText);
        Assert.False(evidence.Edited);
        Assert.True(evidence.Confirmed);
        Assert.Equal(2, evidence.Page);
        Assert.Equal(0.3, evidence.BboxW);
    }

    [Fact]
    public void An_edited_measurement_stores_the_final_value_and_keeps_the_original_in_evidence()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("impact_energy", "Impact energy", "Impact",
            value: new MeasurementValue("60", "J", "-20 C"),
            original: new MeasurementValue("54", "J", "-40 C")));

        var stored = Map(cert);

        var row = Assert.Single(stored.Measurements);
        Assert.Equal("60", row.Value);
        Assert.Equal("-20 C", row.Condition);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:impact_energy");
        Assert.Equal("54 J (-40 C)", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
        Assert.False(evidence.Confirmed);
    }

    [Fact]
    public void An_added_measurement_has_no_original_and_is_marked_edited()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("lot_number", "Lot number", "Product",
            value: new MeasurementValue("L-77"), original: null, page: 0));

        var stored = Map(cert);

        Assert.Equal("L-77", Assert.Single(stored.Measurements).Value);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:lot_number");
        Assert.Null(evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
        Assert.Equal(0, evidence.Page);
        Assert.Null(evidence.BboxX);
    }

    [Fact]
    public void A_removed_measurement_has_no_row_but_leaves_evidence_of_what_the_model_said()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment",
            value: null, original: new MeasurementValue("6-8")));

        var stored = Map(cert);

        Assert.Empty(stored.Measurements);
        var evidence = Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:grain_size");
        Assert.Equal("6-8", evidence.ExtractedValueText);
        Assert.True(evidence.Edited);
    }

    [Fact]
    public void A_measurement_that_was_added_and_then_removed_leaves_nothing()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment", value: null, original: null));

        var stored = Map(cert);

        Assert.Empty(stored.Measurements);
        Assert.DoesNotContain(stored.Evidence, e => e.FieldName.StartsWith("Measurement:"));
    }

    [Fact]
    public void Other_measurements_are_named_by_their_label()
    {
        var cert = Cert();
        var value = new MeasurementValue("5", "%");
        cert.Measurements.Add(Measurement("other", "Delta ferrite", "Other", value, value));

        var stored = Map(cert);

        var row = Assert.Single(stored.Measurements);
        Assert.Equal("other", row.Key);
        Assert.Equal("Delta ferrite", row.Label);
        Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:other:Delta ferrite");
    }

    [Fact]
    public void A_duplicated_measurement_is_mapped_once_so_the_unique_indexes_stay_safe()
    {
        var cert = Cert();
        var a = new MeasurementValue("6");
        var b = new MeasurementValue("7");
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment", a, a));
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment", b, b));

        var stored = Map(cert);

        Assert.Equal("6", Assert.Single(stored.Measurements).Value);
        Assert.Single(stored.Evidence, e => e.FieldName == "Measurement:grain_size");
    }

    [Fact]
    public void Evidence_names_for_measurements_never_collide_with_field_names()
    {
        var cert = Cert();
        var value = new MeasurementValue("1");
        foreach (var kind in MeasurementCatalog.Kinds)
        {
            cert.Measurements.Add(Measurement(kind.Key, kind.Label, kind.Category, value, value));
        }
        cert.Chemistry.NitrogenPct = F<decimal?>(0.074m);

        var stored = Map(cert);

        var names = stored.Evidence.Select(e => e.FieldName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal(MeasurementCatalog.Kinds.Count, stored.Measurements.Count);
    }
}
