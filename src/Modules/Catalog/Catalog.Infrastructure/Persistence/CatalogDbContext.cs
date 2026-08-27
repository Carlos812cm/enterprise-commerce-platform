using Microsoft.EntityFrameworkCore;
using Catalog.Infrastructure.Persistence.Records;

namespace Catalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options)
    : DbContext(options)
{
    public const string Schema = "catalog";

    internal DbSet<ProductRecord> ProductRecords =>
        Set<ProductRecord>();

    internal DbSet<OutboxMessageRecord> OutboxMessages =>
    Set<OutboxMessageRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(CatalogDbContext).Assembly);
    }
}
