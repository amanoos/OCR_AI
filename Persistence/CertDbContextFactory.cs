using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SteelCertExtraction.Persistence;

/// <summary>
/// Used ONLY by `dotnet ef` (migrations add/script/database update) so the
/// tooling doesn't have to boot the whole Blazor app. The running app gets
/// its DbContext from DI (see AddCertPersistence).
///
/// For `migrations add` / `migrations script` no connection is made, so the
/// placeholder below is harmless. To run `dotnet ef database update` against a
/// real server, set the CERTDB_CONNECTION environment variable in your shell —
/// never put a password in this file.
/// </summary>
public class CertDbContextFactory : IDesignTimeDbContextFactory<CertDbContext>
{
    public CertDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CERTDB_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=CertExtraction;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<CertDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new CertDbContext(options);
    }
}
