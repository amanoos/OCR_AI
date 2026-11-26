using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;
using SteelCertExtraction.Services;

namespace SteelCertExtraction.Tests;

/// <summary>
/// The store with the "more properties" fields and measurements: validation on save
/// and full round trips (SQLite in-memory, which enforces the unique indexes).
/// </summary>
public sealed class CertificateStoreMorePropertiesTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CertificateStoreMorePropertiesTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CertDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CertDbContext>().UseSqlite(_connection).Options);

    private sealed class Factory(CertificateStoreMorePropertiesTests owner) : IDbContextFactory<CertDbContext>
    {
        public CertDbContext CreateDbContext() => owner.NewContext();
    }

    private CertificateStore Store() => new(new Factory(this));

    // --- helpers ---

    private static ExtractedField<T> Field<T>(T value, bool confirmed = false, int page = 1) =>
        new() { Value = value, OriginalValue = value, Confirmed = confirmed, PageNumber = page };

    private static SteelCertificate Cert(string heat = "H1") => new()
    {
        HeatNumber = Field(heat, confirmed: true),
        Grade = Field("304L", confirmed: true),
    };

    private static ExtractedMeasurement Measurement(string key, string label, string category, MeasurementValue? value,
        MeasurementValue? original = null, bool confirmed = false) => new()
    {
        Key = key,
        Label = label,
        Category = category,
        Field = new ExtractedField<MeasurementValue> { Value = value, OriginalValue = original, Confirmed = confirmed, PageNumber = 1 },
    };

    private static ExtractedMeasurement Unedited(string key, string label, string category, MeasurementValue value) =>
        Measurement(key, label, category, value, original: value);

    private static ExtractionResult Extraction() => new()
    {
        DocumentId = Guid.NewGuid(),
        SourceFileName = "mtr.pdf",
        PageCount = 1,
        ExtractedAtUtc = DateTime.UtcNow,
        ModelUsed = "claude-test",
    };

    private int CountCerts()
    {
        using var db = NewContext();
        return db.Certificates.Count();
    }

    private async Task<SaveCertificateResult.Invalid> ExpectInvalid(SteelCertificate cert)
    {
        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");
        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Equal(0, CountCerts());
        return invalid;
    }

    // --- validation of the new typed fields ---

    [Theory]
    [InlineData("NitrogenPct", 150, "Nitrogen")]
    [InlineData("CobaltPct", -1, "Cobalt")]
    [InlineData("BoronPct", 100.5, "Boron")]
    [InlineData("OxygenPct", 101, "Oxygen")]
    [InlineData("CarbonEquivalent", 1000, "Carbon Equivalent")]
    public async Task Out_of_range_new_elements_are_rejected(string key, double value, string label)
    {
        var cert = Cert();
        var field = Field<decimal?>((decimal)value);
        switch (key)
        {
            case "NitrogenPct": cert.Chemistry.NitrogenPct = field; break;
            case "CobaltPct": cert.Chemistry.CobaltPct = field; break;
            case "BoronPct": cert.Chemistry.BoronPct = field; break;
            case "OxygenPct": cert.Chemistry.OxygenPct = field; break;
            default: cert.Chemistry.CarbonEquivalent = field; break;
        }

        var invalid = await ExpectInvalid(cert);

        Assert.Contains($"{label} must be between 0 and 100.", invalid.Reasons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(2000.01)]
    public async Task Out_of_range_hardness_is_rejected(double value)
    {
        var cert = Cert();
        cert.Mechanical.HardnessValue = Field<decimal?>((decimal)value);

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Hardness must be greater than 0 and at most 2000.", invalid.Reasons);
    }

    [Fact]
    public async Task An_over_long_hardness_scale_is_rejected()
    {
        var cert = Cert();
        cert.Mechanical.HardnessScale = Field(new string('S', 21));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Hardness Scale must be at most 20 characters.", invalid.Reasons);
    }

    // --- validation of measurements ---

    [Fact]
    public async Task Over_long_measurement_parts_are_rejected_and_name_the_measurement()
    {
        var cert = Cert();
        cert.Measurements.Add(Unedited("heat_treatment", "Heat treatment", "HeatTreatment",
            new MeasurementValue(new string('v', 201), new string('u', 21), new string('c', 201))));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Heat treatment: value must be at most 200 characters.", invalid.Reasons);
        Assert.Contains("Heat treatment: unit must be at most 20 characters.", invalid.Reasons);
        Assert.Contains("Heat treatment: condition must be at most 200 characters.", invalid.Reasons);
    }

    [Fact]
    public async Task An_unknown_measurement_type_is_rejected()
    {
        var cert = Cert();
        cert.Measurements.Add(Unedited("made_up_key", "Made up", "Other", new MeasurementValue("1")));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains(invalid.Reasons, r => r.Contains("unknown measurement type 'made_up_key'"));
    }

    [Fact]
    public async Task A_custom_measurement_needs_a_label()
    {
        var cert = Cert();
        cert.Measurements.Add(Unedited("other", "  ", "Other", new MeasurementValue("5")));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains(invalid.Reasons, r => r.Contains("label is required"));
    }

    [Fact]
    public async Task The_same_measurement_twice_is_rejected_instead_of_hitting_the_database_constraint()
    {
        var cert = Cert();
        cert.Measurements.Add(Unedited("grain_size", "Grain size", "HeatTreatment", new MeasurementValue("6")));
        cert.Measurements.Add(Unedited("grain_size", "Grain size", "HeatTreatment", new MeasurementValue("7")));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Grain size: appears more than once.", invalid.Reasons);
    }

    [Fact]
    public async Task Two_other_measurements_with_different_labels_are_fine()
    {
        var cert = Cert();
        cert.Measurements.Add(Unedited("other", "Delta ferrite", "Other", new MeasurementValue("5", "%")));
        cert.Measurements.Add(Unedited("other", "Magnetic permeability", "Other", new MeasurementValue("1.02")));

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
    }

    [Fact]
    public async Task A_removed_measurement_is_not_validated_or_saved()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment", value: null,
            original: new MeasurementValue(new string('v', 500))));   // the model's original would have been too long

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
    }

    // --- round trips ---

    [Fact]
    public async Task New_typed_values_and_measurements_round_trip_through_the_real_store()
    {
        var cert = Cert("MORE-1");
        cert.Chemistry.NitrogenPct = Field<decimal?>(0.074m, confirmed: true);
        cert.Chemistry.CobaltPct = Field<decimal?>(0.29m);
        cert.Mechanical.HardnessValue = Field<decimal?>(163m);
        cert.Mechanical.HardnessScale = Field("HB");
        cert.Measurements.Add(Unedited("grain_size", "Grain size", "HeatTreatment", new MeasurementValue("6-8")));
        cert.Measurements.Add(Measurement("impact_energy", "Impact energy", "Impact",
            value: new MeasurementValue("60", "J", "-20 C"), original: new MeasurementValue("54", "J", "-40 C"), confirmed: true));

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Evidence).Include(c => c.Measurements).Single();

        Assert.Equal(0.074m, row.NitrogenPct);
        Assert.Equal(0.29m, row.CobaltPct);
        Assert.Equal(163m, row.HardnessValue);
        Assert.Equal("HB", row.HardnessScale);
        Assert.Null(row.BoronPct);   // never 0

        Assert.Equal(2, row.Measurements.Count);
        Assert.Equal("6-8", row.Measurements.Single(m => m.Key == "grain_size").Value);
        var impact = row.Measurements.Single(m => m.Key == "impact_energy");
        Assert.Equal(("60", "J", "-20 C"), (impact.Value, impact.Unit, impact.Condition));

        var impactEvidence = row.Evidence.Single(e => e.FieldName == "Measurement:impact_energy");
        Assert.Equal("54 J (-40 C)", impactEvidence.ExtractedValueText);
        Assert.True(impactEvidence.Edited);
        Assert.True(impactEvidence.Confirmed);
        Assert.False(row.Evidence.Single(e => e.FieldName == "Measurement:grain_size").Edited);
        Assert.True(row.Evidence.Single(e => e.FieldName == "NitrogenPct").Confirmed);
        Assert.False(row.Evidence.Single(e => e.FieldName == "CobaltPct").Confirmed);
    }

    [Fact]
    public async Task A_removed_measurement_leaves_evidence_but_no_measurement_row()
    {
        var cert = Cert();
        cert.Measurements.Add(Measurement("grain_size", "Grain size", "HeatTreatment", value: null, original: new MeasurementValue("6-8")));
        cert.Measurements.Add(Unedited("product_form", "Product form", "Product", new MeasurementValue("Round bar")));

        await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Evidence).Include(c => c.Measurements).Single();
        Assert.Equal("product_form", Assert.Single(row.Measurements).Key);
        var removed = row.Evidence.Single(e => e.FieldName == "Measurement:grain_size");
        Assert.Equal("6-8", removed.ExtractedValueText);
        Assert.True(removed.Edited);
    }

    [Fact]
    public async Task Deleting_a_saved_certificate_removes_its_measurements_and_evidence()
    {
        var cert = Cert("GONE");
        cert.Measurements.Add(Unedited("grain_size", "Grain size", "HeatTreatment", new MeasurementValue("6-8")));
        await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        using (var db = NewContext())
        {
            db.Certificates.Remove(db.Certificates.Single());
            db.SaveChanges();
        }

        using var read = NewContext();
        Assert.Empty(read.Measurements);
        Assert.Empty(read.FieldEvidence);
    }

    [Fact]
    public async Task Certificates_without_any_new_data_save_exactly_as_before()
    {
        var cert = Cert();
        cert.Chemistry.CarbonPct = Field<decimal?>(0.014m, confirmed: true);

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Measurements).Single();
        Assert.Empty(row.Measurements);
        Assert.Null(row.NitrogenPct);
        Assert.Equal(0.014m, row.CarbonPct);
    }
}
