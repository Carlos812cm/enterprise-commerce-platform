using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface IQueryDispatcher
{
    Task<Result<TResponse>> DispatchAsync<TResponse>(
        Query<TResponse> query,
        CancellationToken cancellationToken = default);
}
