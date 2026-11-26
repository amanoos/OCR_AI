using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SteelCertExtraction.Persistence;

namespace SteelCertExtraction.Tests;

public class PersistenceRegistrationTests
{
    private static IConfiguration Config(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:CertDb"] = connectionString })
            .Build();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_connection_string_throws_naming_the_key(string? value)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddCertPersistence(Config(value)));

        Assert.Contains("ConnectionStrings:CertDb", ex.Message);
        Assert.Contains("user-secrets", ex.Message);
    }

    [Fact]
    public void Valid_config_registers_store_and_a_SqlServer_context_factory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCertPersistence(Config("Server=localhost,1433;Database=CertExtraction;User Id=x;Password=x;TrustServerCertificate=True"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<CertificateStore>(scope.ServiceProvider.GetRequiredService<ICertificateStore>());

        // Creating a context does not open a connection.
        using var db = provider.GetRequiredService<IDbContextFactory<CertDbContext>>().CreateDbContext();
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", db.Database.ProviderName);
    }
}
