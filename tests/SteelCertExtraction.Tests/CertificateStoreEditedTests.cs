using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

/// <summary>
/// Store behavior for reviewer edits and for the value rules re-checked on save
/// (SQLite in-memory, which enforces the unique index).
/// </summary>
public sealed class CertificateStoreEditedTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CertificateStoreEditedTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CertDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CertDbContext>().UseSqlite(_connection).Options);

    private sealed class Factory(CertificateStoreEditedTests owner) : IDbContextFactory<CertDbContext>
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
        Chemistry = new ChemistryComposition { CarbonPct = Field<decimal?>(0.025m, confirmed: true) },
    };

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

    // --- value rules re-checked on save ---

    [Fact]
    public async Task Out_of_range_chemistry_is_rejected()
    {
        var cert = Cert();
        cert.Chemistry.CarbonPct!.Value = 150m;

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Carbon must be between 0 and 100.", invalid.Reasons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000000.01)]
    public async Task Out_of_range_strength_is_rejected(double psi)
    {
        var cert = Cert();
        cert.Mechanical.YieldStrengthPsi = Field<decimal?>((decimal)psi);

        var invalid = await ExpectInvalid(cert);

        Assert.Contains(invalid.Reasons, r => r.StartsWith("Yield Strength (psi)"));
    }

    [Fact]
    public async Task Over_long_text_is_rejected_instead_of_becoming_a_sql_error()
    {
        var cert = Cert();
        cert.Specification = Field(new string('S', 201));
        cert.Grade = Field(new string('G', 101), confirmed: true);

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Specification must be at most 200 characters.", invalid.Reasons);
        Assert.Contains("Grade must be at most 100 characters.", invalid.Reasons);
    }

    [Fact]
    public async Task Over_long_heat_number_is_rejected()
    {
        var invalid = await ExpectInvalid(Cert(heat: new string('H', 101)));

        Assert.Contains("Heat Number must be at most 100 characters.", invalid.Reasons);
    }

    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(2099, 1, 1)]
    public async Task Out_of_range_dates_are_rejected(int year, int month, int day)
    {
        var cert = Cert();
        cert.CertDate = Field<DateOnly?>(new DateOnly(year, month, day));

        var invalid = await ExpectInvalid(cert);

        Assert.Contains(invalid.Reasons, r => r.StartsWith("Cert Date must be between 1950-01-01 and today"));
    }

    [Fact]
    public async Task Every_violation_is_listed_not_just_the_first()
    {
        var cert = Cert(heat: "  ");
        cert.Chemistry.CarbonPct!.Value = -1m;
        cert.Chemistry.NickelPct = Field<decimal?>(500m);

        var invalid = await ExpectInvalid(cert);

        Assert.Contains(invalid.Reasons, r => r.Contains("Heat Number is missing"));
        Assert.Contains(invalid.Reasons, r => r.StartsWith("Carbon"));
        Assert.Contains(invalid.Reasons, r => r.StartsWith("Nickel"));
    }

    [Fact]
    public async Task Boundary_values_are_accepted()
    {
        var cert = Cert();
        cert.Chemistry.CarbonPct!.Value = 0m;
        cert.Chemistry.ManganesePct = Field<decimal?>(100m);
        cert.Mechanical.YieldStrengthPsi = Field<decimal?>(0.01m);
        cert.Mechanical.TensileStrengthPsi = Field<decimal?>(1000000m);
        cert.CertDate = Field<DateOnly?>(DateOnly.FromDateTime(DateTime.UtcNow));
        cert.Specification = Field(new string('S', 200));

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
    }

    [Fact]
    public async Task Cleared_optional_values_are_fine()
    {
        var cert = Cert();
        cert.Chemistry.CarbonPct!.Value = null;      // cleared by the reviewer
        cert.CertDate = new ExtractedField<DateOnly?> { Value = null, OriginalValue = new DateOnly(2021, 11, 5) };

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
    }

    // --- edited / added / cleared round trip through the real store ---

    [Fact]
    public async Task Edited_added_and_cleared_values_round_trip_with_originals_and_flags()
    {
        var cert = Cert(heat: "  h-original ");
        cert.Chemistry.CarbonPct!.Value = 0.02m;                 // edited: model said 0.025
        cert.Chemistry.CarbonPct.Confirmed = true;               // reviewer re-ticked it
        cert.Chemistry.ChromiumPct = Field<decimal?>(18.02m);    // model said 18.02...
        cert.Chemistry.ChromiumPct.Value = null;                 // ...reviewer cleared it
        cert.Chemistry.VanadiumPct = new ExtractedField<decimal?> { Value = 0.05m, OriginalValue = null }; // added, unconfirmed
        cert.HeatNumber!.Value = "h-final";                      // edited heat number
        cert.Specification = Field("ASTM A240");                 // untouched, unconfirmed

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(result);
        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Evidence).Single();

        // Final values in the certificate columns
        Assert.Equal("H-FINAL", row.HeatNumber);
        Assert.Equal(0.02m, row.CarbonPct);
        Assert.Null(row.ChromiumPct);
        Assert.Equal(0.05m, row.VanadiumPct);

        // Evidence: original text + Edited flag + Confirmed as left by the reviewer
        var heat = row.Evidence.Single(e => e.FieldName == "HeatNumber");
        Assert.Equal("  h-original ", heat.ExtractedValueText);
        Assert.True(heat.Edited);

        var carbon = row.Evidence.Single(e => e.FieldName == "CarbonPct");
        Assert.Equal("0.025", carbon.ExtractedValueText);
        Assert.True(carbon.Edited);
        Assert.True(carbon.Confirmed);

        var chromium = row.Evidence.Single(e => e.FieldName == "ChromiumPct");
        Assert.Equal("18.02", chromium.ExtractedValueText);
        Assert.True(chromium.Edited);

        var vanadium = row.Evidence.Single(e => e.FieldName == "VanadiumPct");
        Assert.Null(vanadium.ExtractedValueText);
        Assert.True(vanadium.Edited);
        Assert.False(vanadium.Confirmed);

        // Untouched
        var grade = row.Evidence.Single(e => e.FieldName == "Grade");
        Assert.False(grade.Edited);
        var spec = row.Evidence.Single(e => e.FieldName == "Specification");
        Assert.False(spec.Edited);
        Assert.False(spec.Confirmed);
    }

    [Fact]
    public async Task Edited_required_field_must_be_confirmed_again()
    {
        var cert = Cert();
        cert.HeatNumber!.Value = "H-EDITED";
        cert.HeatNumber.Confirmed = false;                       // the UI un-ticks on edit

        var invalid = await ExpectInvalid(cert);

        Assert.Contains("Heat Number has not been confirmed.", invalid.Reasons);
    }

    [Fact]
    public async Task Editing_the_heat_number_to_an_existing_one_is_a_duplicate_and_overwrites_nothing()
    {
        var store = Store();
        await store.SaveConfirmedAsync(Cert(heat: "H-EXISTING"), Extraction(), "/first.pdf");

        var second = Cert(heat: "H-NEW");
        second.HeatNumber!.Value = " h-existing ";                // reviewer edited it onto an existing heat
        var result = await store.SaveConfirmedAsync(second, Extraction(), "/second.pdf");

        var dup = Assert.IsType<SaveCertificateResult.Duplicate>(result);
        Assert.Equal("H-EXISTING", dup.Existing.HeatNumber);
        Assert.Equal(1, CountCerts());
        using var db = NewContext();
        Assert.Equal("/first.pdf", db.Certificates.Single().StoredPdfPath);
    }
}
