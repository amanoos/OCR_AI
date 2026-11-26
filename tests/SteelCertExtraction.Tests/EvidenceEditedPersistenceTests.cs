using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

/// <summary>Checks the new `Edited` column round-trips through EF (SQLite in-memory).</summary>
public sealed class EvidenceEditedPersistenceTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public EvidenceEditedPersistenceTests()
    {
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private CertDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CertDbContext>().UseSqlite(_connection).Options);

    private static StoredCertificate Cert(params CertificateFieldEvidence[] evidence) => new()
    {
        HeatNumber = "H1",
        Grade = "304L",
        SourceFileName = "f.pdf",
        StoredPdfPath = "/f.pdf",
        ModelUsed = "m",
        Evidence = evidence.ToList(),
    };

    [Fact]
    public void Edited_true_and_false_both_round_trip()
    {
        using (var db = NewContext())
        {
            db.Certificates.Add(Cert(
                new CertificateFieldEvidence { FieldName = "CarbonPct", ExtractedValueText = "0.025", Edited = true },
                new CertificateFieldEvidence { FieldName = "NickelPct", ExtractedValueText = "8.57", Edited = false }));
            db.SaveChanges();
        }

        using var read = NewContext();
        var evidence = read.FieldEvidence.OrderBy(e => e.FieldName).ToList();
        Assert.True(evidence.Single(e => e.FieldName == "CarbonPct").Edited);
        Assert.False(evidence.Single(e => e.FieldName == "NickelPct").Edited);
    }

    [Fact]
    public void Edited_defaults_to_false_when_not_set()
    {
        using (var db = NewContext())
        {
            db.Certificates.Add(Cert(new CertificateFieldEvidence { FieldName = "CarbonPct", ExtractedValueText = "0.025" }));
            db.SaveChanges();
        }

        using var read = NewContext();
        Assert.False(read.FieldEvidence.Single().Edited);
    }

    [Fact]
    public void An_added_value_can_be_stored_with_no_original_text()
    {
        using (var db = NewContext())
        {
            db.Certificates.Add(Cert(new CertificateFieldEvidence { FieldName = "VanadiumPct", ExtractedValueText = null, Edited = true }));
            db.SaveChanges();
        }

        using var read = NewContext();
        var row = read.FieldEvidence.Single();
        Assert.Null(row.ExtractedValueText);
        Assert.True(row.Edited);
    }
}
