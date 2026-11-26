using Microsoft.EntityFrameworkCore;

namespace SteelCertExtraction.Persistence;

public class CertDbContext : DbContext
{
    public CertDbContext(DbContextOptions<CertDbContext> options) : base(options) { }

    public DbSet<StoredCertificate> Certificates => Set<StoredCertificate>();
    public DbSet<CertificateFieldEvidence> FieldEvidence => Set<CertificateFieldEvidence>();
    public DbSet<CertificateMeasurement> Measurements => Set<CertificateMeasurement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredCertificate>(e =>
        {
            e.ToTable("StoredCertificates");
            e.HasKey(c => c.Id);

            // Unique on the normalized heat number: the final guard against duplicates,
            // including a race between two concurrent saves.
            e.Property(c => c.HeatNumber).IsRequired().HasMaxLength(100);
            e.HasIndex(c => c.HeatNumber).IsUnique();

            e.Property(c => c.Grade).IsRequired().HasMaxLength(100);
            e.Property(c => c.Specification).HasMaxLength(200);
            e.Property(c => c.CertifyingLab).HasMaxLength(200);
            e.Property(c => c.PurchaseOrderNumber).HasMaxLength(100);
            e.Property(c => c.ReductionOfAreaPct).HasMaxLength(50);

            // Percentages: up to 99.9999. Strengths: up to 9,999,999,999.99 psi.
            foreach (var chem in new[]
            {
                nameof(StoredCertificate.CarbonPct), nameof(StoredCertificate.ManganesePct),
                nameof(StoredCertificate.PhosphorusPct), nameof(StoredCertificate.SulfurPct),
                nameof(StoredCertificate.SiliconPct), nameof(StoredCertificate.CopperPct),
                nameof(StoredCertificate.NickelPct), nameof(StoredCertificate.ChromiumPct),
                nameof(StoredCertificate.MolybdenumPct), nameof(StoredCertificate.VanadiumPct),
                nameof(StoredCertificate.NitrogenPct), nameof(StoredCertificate.CobaltPct),
                nameof(StoredCertificate.BoronPct), nameof(StoredCertificate.AluminumPct),
                nameof(StoredCertificate.TitaniumPct), nameof(StoredCertificate.NiobiumPct),
                nameof(StoredCertificate.TungstenPct), nameof(StoredCertificate.TinPct),
                nameof(StoredCertificate.LeadPct), nameof(StoredCertificate.ArsenicPct),
                nameof(StoredCertificate.AntimonyPct), nameof(StoredCertificate.CalciumPct),
                nameof(StoredCertificate.OxygenPct), nameof(StoredCertificate.HydrogenPct),
                nameof(StoredCertificate.TantalumPct), nameof(StoredCertificate.CarbonEquivalent),
                nameof(StoredCertificate.ElongationPct),
            })
            {
                e.Property(chem).HasPrecision(9, 4);
            }
            e.Property(c => c.HardnessValue).HasPrecision(9, 2);
            e.Property(c => c.HardnessScale).HasMaxLength(20);
            e.Property(c => c.YieldStrengthPsi).HasPrecision(12, 2);
            e.Property(c => c.TensileStrengthPsi).HasPrecision(12, 2);

            e.Property(c => c.SourceFileName).IsRequired().HasMaxLength(260);
            e.Property(c => c.StoredPdfPath).IsRequired().HasMaxLength(500);
            e.Property(c => c.ModelUsed).IsRequired().HasMaxLength(200);

            e.HasMany(c => c.Evidence)
                .WithOne(f => f.Certificate)
                .HasForeignKey(f => f.CertificateId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.Measurements)
                .WithOne(m => m.Certificate)
                .HasForeignKey(m => m.CertificateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CertificateMeasurement>(e =>
        {
            e.ToTable("CertificateMeasurements");
            e.HasKey(m => m.Id);
            e.Property(m => m.Key).IsRequired().HasMaxLength(50);
            e.Property(m => m.Label).IsRequired().HasMaxLength(100);
            e.Property(m => m.Category).IsRequired().HasMaxLength(50);
            e.Property(m => m.Value).IsRequired().HasMaxLength(200);
            e.Property(m => m.Unit).HasMaxLength(20);
            e.Property(m => m.Condition).HasMaxLength(200);

            // One measurement per catalog key; "other" entries are unique by label.
            e.HasIndex(m => new { m.CertificateId, m.Key, m.Label }).IsUnique();
        });

        modelBuilder.Entity<CertificateFieldEvidence>(e =>
        {
            e.ToTable("CertificateFieldEvidence");
            e.HasKey(f => f.Id);
            // 150, not 100: measurement evidence is named "Measurement:other:<label>" (label up to 100).
            e.Property(f => f.FieldName).IsRequired().HasMaxLength(150);
            e.Property(f => f.ExtractedValueText).HasMaxLength(500);
            e.Property(f => f.Edited).HasDefaultValue(false);
            e.HasIndex(f => new { f.CertificateId, f.FieldName }).IsUnique();
        });
    }
}
