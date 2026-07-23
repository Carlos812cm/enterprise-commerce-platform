namespace Catalog.Application.Abstractions.Persistence;

public interface ICatalogUnitOfWork
{
    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}
