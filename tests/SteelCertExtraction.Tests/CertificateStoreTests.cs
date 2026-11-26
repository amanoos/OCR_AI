using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SteelCertExtraction.Domain.Models;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

/// <summary>
/// Runs the store against SQLite in-memory (a single shared open connection),
/// which — unlike EF's InMemory provider — enforces the unique index.
/// </summary>
public sealed class CertificateStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CertificateStoreTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CertDbContext NewContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<CertDbContext>().UseSqlite(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new CertDbContext(builder.Options);
    }

    private sealed class Factory(CertificateStoreTests owner, IInterceptor? interceptor = null)
        : IDbContextFactory<CertDbContext>
    {
        public CertDbContext CreateDbContext() => owner.NewContext(interceptor);
    }

    private CertificateStore Store(IInterceptor? interceptor = null) => new(new Factory(this, interceptor));

    // --- helpers ---

    private static ExtractedField<T> F<T>(T value, bool confirmed = false, int page = 1) =>
        new() { Value = value, OriginalValue = value, Confirmed = confirmed, PageNumber = page };

    private static SteelCertificate Cert(
        string heat = "H123", string grade = "304L", bool heatConfirmed = true, bool gradeConfirmed = true) => new()
    {
        HeatNumber = F(heat, heatConfirmed),
        Grade = F(grade, gradeConfirmed),
        Specification = F("ASTM A240"), // deliberately NOT confirmed
        Chemistry = new ChemistryComposition
        {
            CarbonPct = F<decimal?>(0.025m, confirmed: true),
            VanadiumPct = F<decimal?>(null, page: 0), // not on this cert
        },
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

    // --- tests ---

    [Fact]
    public async Task Valid_cert_is_saved_with_values_and_evidence()
    {
        var result = await Store().SaveConfirmedAsync(Cert(), Extraction(), "/uploaded-certs/mtr.pdf");

        var saved = Assert.IsType<SaveCertificateResult.Saved>(result);
        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Evidence).Single();
        Assert.Equal(saved.Id, row.Id);
        Assert.Equal("H123", row.HeatNumber);
        Assert.Equal("304L", row.Grade);
        Assert.Equal(0.025m, row.CarbonPct);
        Assert.Equal("mtr.pdf", row.SourceFileName);
        Assert.Equal("/uploaded-certs/mtr.pdf", row.StoredPdfPath);
        Assert.True(row.ConfirmedAtUtc > DateTime.UtcNow.AddMinutes(-1));
        Assert.Contains(row.Evidence, e => e.FieldName == "HeatNumber" && e.Confirmed);
        Assert.Contains(row.Evidence, e => e.FieldName == "CarbonPct");
    }

    [Fact]
    public async Task Missing_numeric_values_are_stored_as_NULL_and_unconfirmed_optionals_marked()
    {
        await Store().SaveConfirmedAsync(Cert(), Extraction(), "/p.pdf");

        using var db = NewContext();
        var row = db.Certificates.Include(c => c.Evidence).Single();
        Assert.Null(row.VanadiumPct);
        Assert.Null(row.NickelPct);
        Assert.DoesNotContain(row.Evidence, e => e.FieldName == "VanadiumPct");
        var spec = Assert.Single(row.Evidence, e => e.FieldName == "Specification");
        Assert.False(spec.Confirmed);
        Assert.Equal("ASTM A240", row.Specification);
    }

    [Fact]
    public async Task Missing_heat_number_is_invalid_and_nothing_is_saved()
    {
        var cert = Cert();
        cert.HeatNumber = F<string>(null!, confirmed: false);

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Contains(invalid.Reasons, r => r.Contains("Heat Number is missing"));
        Assert.Equal(0, CountCerts());
    }

    [Fact]
    public async Task Whitespace_heat_number_counts_as_missing()
    {
        var result = await Store().SaveConfirmedAsync(Cert(heat: "   "), Extraction(), "/p.pdf");

        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Contains(invalid.Reasons, r => r.Contains("Heat Number is missing"));
        Assert.Equal(0, CountCerts());
    }

    [Fact]
    public async Task Unconfirmed_heat_number_is_invalid()
    {
        var result = await Store().SaveConfirmedAsync(Cert(heatConfirmed: false), Extraction(), "/p.pdf");

        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Contains(invalid.Reasons, r => r.Contains("Heat Number has not been confirmed"));
        Assert.Equal(0, CountCerts());
    }

    [Fact]
    public async Task Unconfirmed_grade_is_invalid()
    {
        var result = await Store().SaveConfirmedAsync(Cert(gradeConfirmed: false), Extraction(), "/p.pdf");

        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Contains(invalid.Reasons, r => r.Contains("Grade has not been confirmed"));
        Assert.Equal(0, CountCerts());
    }

    [Fact]
    public async Task Missing_grade_and_heat_report_both_reasons()
    {
        var cert = new SteelCertificate(); // nothing extracted at all

        var result = await Store().SaveConfirmedAsync(cert, Extraction(), "/p.pdf");

        var invalid = Assert.IsType<SaveCertificateResult.Invalid>(result);
        Assert.Equal(2, invalid.Reasons.Count);
        Assert.Equal(0, CountCerts());
    }

    [Fact]
    public async Task Same_heat_number_is_a_duplicate_ignoring_case_and_whitespace()
    {
        var store = Store();
        await store.SaveConfirmedAsync(Cert(heat: "H123"), Extraction(), "/first.pdf");

        var result = await store.SaveConfirmedAsync(Cert(heat: "  h123 "), Extraction(), "/second.pdf");

        var dup = Assert.IsType<SaveCertificateResult.Duplicate>(result);
        Assert.Equal("H123", dup.Existing.HeatNumber);
        Assert.Equal("304L", dup.Existing.Grade);
        Assert.Equal("mtr.pdf", dup.Existing.SourceFileName);
        Assert.Equal(1, CountCerts());
    }

    [Fact]
    public async Task Different_heat_numbers_from_one_pdf_both_save()
    {
        var store = Store();
        var extraction = Extraction();

        var a = await store.SaveConfirmedAsync(Cert(heat: "H1"), extraction, "/same.pdf");
        var b = await store.SaveConfirmedAsync(Cert(heat: "H2"), extraction, "/same.pdf");

        Assert.IsType<SaveCertificateResult.Saved>(a);
        Assert.IsType<SaveCertificateResult.Saved>(b);
        Assert.Equal(2, CountCerts());
    }

    [Fact]
    public async Task Concurrent_insert_hitting_the_unique_index_becomes_Duplicate_not_an_exception()
    {
        // Simulates the race: our duplicate pre-check passes, then another
        // save inserts the same heat number just before our INSERT executes.
        var interceptor = new CompetingInsertInterceptor(_connection, heatNumber: "H123");

        var result = await Store(interceptor).SaveConfirmedAsync(Cert(heat: "H123"), Extraction(), "/p.pdf");

        var dup = Assert.IsType<SaveCertificateResult.Duplicate>(result);
        Assert.Equal("other.pdf", dup.Existing.SourceFileName); // the competing row won
        Assert.True(interceptor.Fired);
        Assert.Equal(1, CountCerts());
    }

    private sealed class CompetingInsertInterceptor(SqliteConnection connection, string heatNumber)
        : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                var options = new DbContextOptionsBuilder<CertDbContext>().UseSqlite(connection).Options;
                await using var other = new CertDbContext(options);
                other.Certificates.Add(new StoredCertificate
                {
                    HeatNumber = heatNumber,
                    Grade = "X",
                    SourceFileName = "other.pdf",
                    StoredPdfPath = "/other.pdf",
                    ModelUsed = "m",
                });
                await other.SaveChangesAsync(cancellationToken);
            }
            return result;
        }
    }
}
