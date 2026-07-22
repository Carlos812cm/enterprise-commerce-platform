using System.Diagnostics;
using Commerce.Domain;
using Microsoft.Extensions.Logging;

namespace Commerce.Application.Messaging.Behaviors;

internal sealed class CommandLoggingBehavior<TCommand, TResponse> :
    ICommandBehavior<TCommand, TResponse>
    where TCommand : Command<TResponse>
{
    private readonly ILogger<
        CommandLoggingBehavior<TCommand, TResponse>> _logger;

    public CommandLoggingBehavior(
        ILogger<
            CommandLoggingBehavior<TCommand, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public int Order =>
        CommandBehaviorOrder.Logging;

    public async Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CommandHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handlerContinuation);

        var commandName = typeof(TCommand).Name;
        var startedTimestamp = Stopwatch.GetTimestamp();

        CommandLogMessages.CommandStarted(
            _logger,
            commandName);

        try
        {
            var result = await handlerContinuation(cancellationToken)
                .ConfigureAwait(false);

            var elapsedMilliseconds =
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds;

            if (result.IsSuccess)
            {
                CommandLogMessages.CommandCompleted(
                    _logger,
                    commandName,
                    elapsedMilliseconds);
            }
            else
            {
                CommandLogMessages.CommandDomainFailure(
                    _logger,
                    commandName,
                    result.Error?.Code ??
                    "unknown-domain-error",
                    elapsedMilliseconds);
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            CommandLogMessages.CommandCancelled(
                _logger,
                commandName,
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds);

            throw;
        }
        catch (Exception exception)
        {
            CommandLogMessages.CommandException(
                _logger,
                commandName,
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                exception);

            throw;
        }
    }
}
