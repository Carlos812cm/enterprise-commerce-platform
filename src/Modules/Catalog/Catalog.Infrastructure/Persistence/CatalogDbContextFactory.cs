using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContextFactory :
    IDesignTimeDbContextFactory<CatalogDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev_password;Pooling=true";

    public CatalogDbContext CreateDbContext(
        string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var connectionString =
            Environment.GetEnvironmentVariable(
                "ConnectionStrings__Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                DefaultConnectionString;
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<CatalogDbContext>();

        CatalogDbContextOptions.Configure(
            optionsBuilder,
            connectionString);

        return new CatalogDbContext(
            optionsBuilder.Options);
    }
}
