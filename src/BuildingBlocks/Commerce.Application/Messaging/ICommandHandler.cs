using Commerce.Domain;

namespace Commerce.Application.Messaging;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : Command<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken);
}
