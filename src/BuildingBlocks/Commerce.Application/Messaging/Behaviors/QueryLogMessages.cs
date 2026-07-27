using Microsoft.Extensions.Logging;

namespace Commerce.Application.Messaging.Behaviors;

internal static class QueryLogMessages
{
    private static readonly Action<
        ILogger,
        string,
        Exception?> StartedMessage =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(
                4_200,
                nameof(QueryStarted)),
            "Handling query {QueryName}.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> CompletedMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(
                4_201,
                nameof(QueryCompleted)),
            "Query {QueryName} completed in {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        string,
        double,
        Exception?> DomainFailureMessage =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Warning,
            new EventId(
                4_202,
                nameof(QueryDomainFailure)),
            "Query {QueryName} failed with domain error {ErrorCode} after {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> CancelledMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(
                4_203,
                nameof(QueryCancelled)),
            "Query {QueryName} was cancelled after {ElapsedMilliseconds:F2} ms.");

    private static readonly Action<
        ILogger,
        string,
        double,
        Exception?> ExceptionMessage =
        LoggerMessage.Define<string, double>(
            LogLevel.Error,
            new EventId(
                4_204,
                nameof(QueryException)),
            "Query {QueryName} threw an exception after {ElapsedMilliseconds:F2} ms.");

    public static void QueryStarted(
        ILogger logger,
        string queryName)
    {
        StartedMessage(
            logger,
            queryName,
            null);
    }

    public static void QueryCompleted(
        ILogger logger,
        string queryName,
        double elapsedMilliseconds)
    {
        CompletedMessage(
            logger,
            queryName,
            elapsedMilliseconds,
            null);
    }

    public static void QueryDomainFailure(
        ILogger logger,
        string queryName,
        string errorCode,
        double elapsedMilliseconds)
    {
        DomainFailureMessage(
            logger,
            queryName,
            errorCode,
            elapsedMilliseconds,
            null);
    }

    public static void QueryCancelled(
        ILogger logger,
        string queryName,
        double elapsedMilliseconds)
    {
        CancelledMessage(
            logger,
            queryName,
            elapsedMilliseconds,
            null);
    }

    public static void QueryException(
        ILogger logger,
        string queryName,
        double elapsedMilliseconds,
        Exception exception)
    {
        ExceptionMessage(
            logger,
            queryName,
            elapsedMilliseconds,
            exception);
    }
}
