using System.Diagnostics;
using Commerce.Domain;
using Microsoft.Extensions.Logging;

namespace Commerce.Application.Messaging.Behaviors;

internal sealed class QueryLoggingBehavior<TQuery, TResponse> :
    IQueryBehavior<TQuery, TResponse>
    where TQuery : Query<TResponse>
{
    private readonly ILogger<
        QueryLoggingBehavior<TQuery, TResponse>> _logger;

    public QueryLoggingBehavior(
        ILogger<
            QueryLoggingBehavior<TQuery, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public int Order =>
        QueryBehaviorOrder.Logging;

    public async Task<Result<TResponse>> HandleAsync(
        TQuery query,
        QueryHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(handlerContinuation);

        var queryName = typeof(TQuery).Name;
        var startedTimestamp = Stopwatch.GetTimestamp();

        QueryLogMessages.QueryStarted(
            _logger,
            queryName);

        try
        {
            var result =
                await handlerContinuation(cancellationToken)
                    .ConfigureAwait(false);

            var elapsedMilliseconds =
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds;

            if (result.IsSuccess)
            {
                QueryLogMessages.QueryCompleted(
                    _logger,
                    queryName,
                    elapsedMilliseconds);
            }
            else
            {
                QueryLogMessages.QueryDomainFailure(
                    _logger,
                    queryName,
                    result.Error?.Code ??
                    "unknown-domain-error",
                    elapsedMilliseconds);
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            QueryLogMessages.QueryCancelled(
                _logger,
                queryName,
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds);

            throw;
        }
        catch (Exception exception)
        {
            QueryLogMessages.QueryException(
                _logger,
                queryName,
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalMilliseconds,
                exception);

            throw;
        }
    }
}
