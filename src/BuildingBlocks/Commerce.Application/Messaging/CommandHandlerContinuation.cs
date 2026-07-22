using Commerce.Domain;

namespace Commerce.Application.Messaging;

public delegate Task<Result<TResponse>>
    CommandHandlerContinuation<TResponse>(
        CancellationToken cancellationToken);
