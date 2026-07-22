using Microsoft.Extensions.Logging;

namespace Commerce.Application.Messaging.Behaviors;

internal static class CommandLogMessages
{
    private static readonly Action<
        ILogger,
        string,
        Exception?> StartedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(
                4_100,
                nameof(CommandStarted)),
            "Handling command {CommandName}.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> CompletedMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(
                4_101,
                nameof(CommandCompleted)),
            "Command {CommandName} completed in {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        string,
        double,
        Exception?> DomainFailureMessage =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Warning,
            new EventId(
                4_102,
                nameof(CommandDomainFailure)),
            "Command {CommandName} failed with domain error {ErrorCode} after {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> CancelledMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(
                4_103,
                nameof(CommandCancelled)),
            "Command {CommandName} was cancelled after {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> ExceptionMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Error,
            new EventId(
                4_104,
                nameof(CommandException)),
            "Command {CommandName} threw an exception after {ElapsedMilliseconds:F2} ms.");

    public static void CommandStarted(
        ILogger logger,
        string commandName)
    {
        StartedMessage(
            logger,
            commandName,
            null);
    }

    public static void CommandCompleted(
        ILogger logger,
        string commandName,
        double elapsedMilliseconds)
    {
        CompletedMessage(
            logger,
            commandName,
            elapsedMilliseconds,
            null);
    }

    public static void CommandDomainFailure(
        ILogger logger,
        string commandName,
        string errorCode,
        double elapsedMilliseconds)
    {
        DomainFailureMessage(
            logger,
            commandName,
            errorCode,
            elapsedMilliseconds,
            null);
    }

    public static void CommandCancelled(
        ILogger logger,
        string commandName,
        double elapsedMilliseconds)
    {
        CancelledMessage(
            logger,
            commandName,
            elapsedMilliseconds,
            null);
    }

    public static void CommandException(
        ILogger logger,
        string commandName,
        double elapsedMilliseconds,
        Exception exception)
    {
        ExceptionMessage(
            logger,
            commandName,
            elapsedMilliseconds,
            exception);
    }
}
