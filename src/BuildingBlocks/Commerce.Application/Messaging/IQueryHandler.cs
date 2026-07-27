using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : Query<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken);
}
