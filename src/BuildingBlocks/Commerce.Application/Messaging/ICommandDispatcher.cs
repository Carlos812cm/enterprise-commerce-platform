using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface ICommandDispatcher
{
    Task<Result<TResponse>> DispatchAsync<TResponse>(
        Command<TResponse> command,
        CancellationToken cancellationToken = default);
}
