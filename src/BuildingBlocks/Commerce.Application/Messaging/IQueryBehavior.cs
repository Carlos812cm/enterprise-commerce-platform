using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface IQueryBehavior<in TQuery, TResponse>
    where TQuery : Query<TResponse>
{
    int Order { get; }

    Task<Result<TResponse>> HandleAsync(
        TQuery query,
        QueryHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken);
}
