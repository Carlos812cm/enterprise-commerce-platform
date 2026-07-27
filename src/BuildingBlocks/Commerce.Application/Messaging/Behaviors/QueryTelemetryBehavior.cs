using System.Diagnostics;
using System.Diagnostics.Metrics;
using Commerce.Application.Diagnostics;
using Commerce.Domain;

namespace Commerce.Application.Messaging.Behaviors;

internal sealed class QueryTelemetryBehavior<TQuery, TResponse> :
    IQueryBehavior<TQuery, TResponse>
    where TQuery : Query<TResponse>
{
    public int Order =>
        QueryBehaviorOrder.Telemetry;

    public async Task<Result<TResponse>> HandleAsync(
        TQuery query,
        QueryHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(handlerContinuation);

        var queryName = typeof(TQuery).Name;
        var startedTimestamp = Stopwatch.GetTimestamp();
        var outcome = "error";

        using var activity =
            ApplicationDiagnostics.ActivitySource.StartActivity(
                $"{queryName}.execute",
                ActivityKind.Internal);

        activity?.SetTag(
            "query.name",
            queryName);

        try
        {
            var result =
                await handlerContinuation(cancellationToken)
                    .ConfigureAwait(false);

            outcome = result.IsSuccess
                ? "success"
                : "failure";

            activity?.SetStatus(
                result.IsSuccess
                    ? ActivityStatusCode.Ok
                    : ActivityStatusCode.Error,
                result.Error?.Code);

            if (result.Error is not null)
            {
                activity?.SetTag(
                    "error.type",
                    result.Error.Code);
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";

            activity?.SetStatus(
                ActivityStatusCode.Error,
                "Query execution was cancelled.");

            throw;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(
                ActivityStatusCode.Error,
                exception.Message);

            activity?.SetTag(
                "error.type",
                exception.GetType().FullName);

            throw;
        }
        finally
        {
            activity?.SetTag(
                "query.outcome",
                outcome);

            var tags = new TagList
            {
                { "query.name", queryName },
                { "query.outcome", outcome }
            };

            ApplicationDiagnostics.QueryExecutions.Add(
                1,
                tags);

            ApplicationDiagnostics.QueryDuration.Record(
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalSeconds,
                tags);
        }
    }
}
