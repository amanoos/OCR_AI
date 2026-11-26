using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

/// <summary>
/// Schema-level checks for the "more properties" columns and the measurements
/// table (SQLite in-memory, which enforces unique indexes and FK cascades).
/// </summary>
public sealed class MeasurementPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public MeasurementPersistenceTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CertDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CertDbContext>().UseSqlite(_connection).Options);

    private static StoredCertificate Cert(string heat = "H1") => new()
    {
        HeatNumber = heat,
        Grade = "304L",
        SourceFileName = "f.pdf",
        StoredPdfPath = "/f.pdf",
        ModelUsed = "m",
    };

    private static CertificateMeasurement Measurement(string key, string label = "", string value = "54", string? unit = "J", string? condition = null) => new()
    {
        Key = key,
        Label = label.Length > 0 ? label : key,
        Category = "Impact",
        Value = value,
        Unit = unit,
        Condition = condition,
    };

    // --- new typed columns ---

    [Fact]
    public void New_typed_columns_round_trip_and_default_to_null()
    {
        using (var db = NewContext())
        {
            var cert = Cert();
            cert.NitrogenPct = 0.074m;
            cert.CobaltPct = 0.29m;
            cert.CarbonEquivalent = 0.42m;
            cert.HardnessValue = 163m;
            cert.HardnessScale = "HB";
            db.Certificates.Add(cert);
            db.SaveChanges();
        }

        using var read = NewContext();
        var row = read.Certificates.Single();
        Assert.Equal(0.074m, row.NitrogenPct);
        Assert.Equal(0.29m, row.CobaltPct);
        Assert.Equal(0.42m, row.CarbonEquivalent);
        Assert.Equal(163m, row.HardnessValue);
        Assert.Equal("HB", row.HardnessScale);

        // Anything not set stays NULL — never 0.
        Assert.Null(row.BoronPct);
        Assert.Null(row.HydrogenPct);
        Assert.Null(row.TantalumPct);
    }

    [Fact]
    public void New_element_columns_use_the_same_precision_as_the_existing_ones()
    {
        using var db = NewContext();
        var entity = db.Model.FindEntityType(typeof(StoredCertificate))!;

        var elements = new[]
        {
            nameof(StoredCertificate.NitrogenPct), nameof(StoredCertificate.CobaltPct),
            nameof(StoredCertificate.BoronPct), nameof(StoredCertificate.AluminumPct),
            nameof(StoredCertificate.TitaniumPct), nameof(StoredCertificate.NiobiumPct),
            nameof(StoredCertificate.TungstenPct), nameof(StoredCertificate.TinPct),
            nameof(StoredCertificate.LeadPct), nameof(StoredCertificate.ArsenicPct),
            nameof(StoredCertificate.AntimonyPct), nameof(StoredCertificate.CalciumPct),
            nameof(StoredCertificate.OxygenPct), nameof(StoredCertificate.HydrogenPct),
            nameof(StoredCertificate.TantalumPct), nameof(StoredCertificate.CarbonEquivalent),
            nameof(StoredCertificate.CarbonPct),      // an existing one, as the reference point
        };

        foreach (var name in elements)
        {
            var property = entity.FindProperty(name)!;
            Assert.Equal(9, property.GetPrecision());
            Assert.Equal(4, property.GetScale());
        }

        var hardness = entity.FindProperty(nameof(StoredCertificate.HardnessValue))!;
        Assert.Equal(9, hardness.GetPrecision());
        Assert.Equal(2, hardness.GetScale());
        Assert.Equal(20, entity.FindProperty(nameof(StoredCertificate.HardnessScale))!.GetMaxLength());
    }

    [Fact]
    public void Evidence_field_names_have_room_for_measurement_names()
    {
        using var db = NewContext();
        var evidence = db.Model.FindEntityType(typeof(CertificateFieldEvidence))!;

        // "Measurement:other:" (18 chars) + a label of up to 100 characters.
        Assert.True(evidence.FindProperty(nameof(CertificateFieldEvidence.FieldName))!.GetMaxLength() >= 118);
    }

    // --- measurements table ---

    [Fact]
    public void Measurements_round_trip_with_and_without_unit_and_condition()
    {
        using (var db = NewContext())
        {
            var cert = Cert();
            cert.Measurements.Add(Measurement("impact_energy", value: "54", unit: "J", condition: "-40 C, 10x10 mm, longitudinal"));
            cert.Measurements.Add(Measurement("grain_size", value: "6-8", unit: null, condition: null));
            db.Certificates.Add(cert);
            db.SaveChanges();
        }

        using var read = NewContext();
        var rows = read.Measurements.OrderBy(m => m.Key).ToList();
        Assert.Equal(2, rows.Count);

        var grain = rows.Single(m => m.Key == "grain_size");
        Assert.Equal("6-8", grain.Value);
        Assert.Null(grain.Unit);
        Assert.Null(grain.Condition);

        var impact = rows.Single(m => m.Key == "impact_energy");
        Assert.Equal("J", impact.Unit);
        Assert.Equal("-40 C, 10x10 mm, longitudinal", impact.Condition);
        Assert.Equal("Impact", impact.Category);
    }

    [Fact]
    public void The_same_key_twice_on_one_certificate_is_rejected()
    {
        using var db = NewContext();
        var cert = Cert();
        cert.Measurements.Add(Measurement("impact_energy", value: "54"));
        cert.Measurements.Add(Measurement("impact_energy", value: "60"));
        db.Certificates.Add(cert);

        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void Other_measurements_with_different_labels_are_allowed_but_the_same_label_is_not()
    {
        using (var db = NewContext())
        {
            var cert = Cert();
            cert.Measurements.Add(Measurement("other", label: "Delta ferrite", value: "5", unit: "%"));
            cert.Measurements.Add(Measurement("other", label: "Magnetic permeability", value: "1.02", unit: null));
            db.Certificates.Add(cert);
            db.SaveChanges();   // fine
        }

        using var db2 = NewContext();
        var cert2 = db2.Certificates.Single();
        cert2.Measurements.Add(Measurement("other", label: "Delta ferrite", value: "6"));

        Assert.Throws<DbUpdateException>(() => db2.SaveChanges());
    }

    [Fact]
    public void Different_certificates_can_each_have_the_same_key()
    {
        using var db = NewContext();
        var a = Cert("H1");
        a.Measurements.Add(Measurement("grain_size", value: "6"));
        var b = Cert("H2");
        b.Measurements.Add(Measurement("grain_size", value: "7"));
        db.Certificates.AddRange(a, b);

        db.SaveChanges();

        Assert.Equal(2, db.Measurements.Count());
    }

    [Fact]
    public void Deleting_a_certificate_deletes_its_measurements()
    {
        using (var db = NewContext())
        {
            var keep = Cert("KEEP");
            keep.Measurements.Add(Measurement("grain_size", value: "6"));
            var drop = Cert("DROP");
            drop.Measurements.Add(Measurement("grain_size", value: "7"));
            drop.Measurements.Add(Measurement("impact_energy", value: "54"));
            db.Certificates.AddRange(keep, drop);
            db.SaveChanges();
        }

        using (var db = NewContext())
        {
            db.Certificates.Remove(db.Certificates.Single(c => c.HeatNumber == "DROP"));
            db.SaveChanges();
        }

        using var read = NewContext();
        var remaining = read.Measurements.Include(m => m.Certificate).ToList();
        var only = Assert.Single(remaining);
        Assert.Equal("KEEP", only.Certificate!.HeatNumber);
    }
}
