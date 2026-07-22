using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface ICommandBehavior<in TCommand, TResponse>
    where TCommand : Command<TResponse>
{
    int Order { get; }

    Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CommandHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken);
}
