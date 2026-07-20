namespace Commerce.Application.Persistence;

public interface IUnitOfWork
{
    Task SaveChangesAsync(
        CancellationToken cancellationToken);
}
