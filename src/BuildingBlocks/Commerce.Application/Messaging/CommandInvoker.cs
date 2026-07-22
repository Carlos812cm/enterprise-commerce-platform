using Commerce.Domain;

namespace Commerce.Application.Messaging;

internal sealed class CommandInvoker<TCommand, TResponse> :
    ICommandInvoker<TResponse>
    where TCommand : Command<TResponse>
{
    private readonly ICommandHandler<TCommand, TResponse> _handler;
    private readonly ICommandBehavior<TCommand, TResponse>[] _behaviors;

    public CommandInvoker(
        ICommandHandler<TCommand, TResponse> handler,
        IEnumerable<ICommandBehavior<TCommand, TResponse>> behaviors)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(behaviors);

        _handler = handler;

        _behaviors = behaviors
            .OrderBy(static behavior => behavior.Order)
            .ToArray();
    }

    public Task<Result<TResponse>> InvokeAsync(
        Command<TResponse> command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        cancellationToken.ThrowIfCancellationRequested();

        if (command is not TCommand typedCommand)
        {
            throw new InvalidOperationException(
                $"The command invoker for '{typeof(TCommand).FullName}' received '{command.GetType().FullName}'.");
        }

        CommandHandlerContinuation<TResponse> pipeline =
            token => _handler.HandleAsync(
                typedCommand,
                token);

        for (var index = _behaviors.Length - 1;
             index >= 0;
             index--)
        {
            var behavior = _behaviors[index];
            var next = pipeline;

            pipeline = token => behavior.HandleAsync(
                typedCommand,
                next,
                token);
        }

        return pipeline(cancellationToken);
    }
}
