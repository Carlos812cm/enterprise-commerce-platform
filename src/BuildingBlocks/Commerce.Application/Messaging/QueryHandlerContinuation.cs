using Commerce.Domain;

namespace Commerce.Application.Messaging;

public delegate Task<Result<TResponse>>
    QueryHandlerContinuation<TResponse>(
        CancellationToken cancellationToken);
