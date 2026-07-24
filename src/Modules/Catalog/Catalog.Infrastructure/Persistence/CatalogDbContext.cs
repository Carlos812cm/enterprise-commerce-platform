using Catalog.Application.Abstractions.Persistence;
using Microsoft.EntityFrameworkCore;
using Catalog.Infrastructure.Persistence.Records;

namespace Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options)
    : DbContext(options),
      ICatalogUnitOfWork
{
    public const string Schema = "catalog";

    internal DbSet<ProductRecord> ProductRecords =>
        Set<ProductRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly);
    }

    async Task ICatalogUnitOfWork.SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        _ = await base
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
