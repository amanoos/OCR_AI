using Microsoft.EntityFrameworkCore;

namespace SteelCertExtraction.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "CertDb";

    /// <summary>
    /// Registers the SQL Server DbContext factory and the certificate store.
    /// Fails fast, naming the key, if ConnectionStrings:CertDb is not set.
    /// Never commit the connection string — use user-secrets or an environment
    /// variable (ConnectionStrings__CertDb).
    /// </summary>
    public static IServiceCollection AddCertPersistence(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not configured. Set it with: " +
                $"dotnet user-secrets set \"ConnectionStrings:{ConnectionStringName}\" \"<connection string>\" " +
                "--project SteelCertExtraction.csproj (or the ConnectionStrings__CertDb environment variable).");
        }

        // A factory (not a scoped DbContext): Blazor Server circuits are
        // long-lived, so each operation gets its own short-lived context.
        services.AddDbContextFactory<CertDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ICertificateStore, CertificateStore>();

        return services;
    }

    /// <summary>
    /// Applies pending EF migrations (creating the database if needed). Call
    /// ONLY in Development; elsewhere run `dotnet ef database update` as a
    /// deliberate deployment step.
    /// </summary>
    public static async Task MigrateCertDatabaseAsync(this IHost host, CancellationToken ct = default)
    {
        var factory = host.Services.GetRequiredService<IDbContextFactory<CertDbContext>>();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CertDatabase");

        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await db.Database.MigrateAsync(ct);
            logger.LogInformation("CertExtraction database is up to date.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not apply migrations to the CertExtraction database. Check that SQL Server is running " +
                "and that ConnectionStrings:{Name} is correct.", ConnectionStringName);
            throw;
        }
    }
}
