using System.Diagnostics;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed class CatalogOutboxMessageProcessor
{
    private readonly CatalogOutboxStore _store;
    private readonly CatalogOutboxDispatcher _dispatcher;

    public CatalogOutboxMessageProcessor(
        CatalogOutboxStore store,
        CatalogOutboxDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(
            store);

        ArgumentNullException.ThrowIfNull(
            dispatcher);

        _store = store;
        _dispatcher = dispatcher;
    }

    public async ValueTask<CatalogOutboxProcessResult>
        ProcessAsync(
            ClaimedCatalogOutboxMessage message,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        var startedTimestamp =
            Stopwatch.GetTimestamp();

        using var activityScope =
            CatalogOutboxActivity.Start(
                message);

        try
        {
            var decodeResult =
                CatalogOutboxMessageDecoder.Decode(
                    message);

            CatalogOutboxProcessResult result;

            if (!decodeResult.Succeeded)
            {
                result =
                    await RecordFailureAsync(
                        message,
                        CatalogOutboxFailureKind.Permanent,
                        GetRequiredErrorCode(
                            decodeResult.ErrorCode),
                        cancellationToken);
            }
            else
            {
                var dispatchResult =
                    await _dispatcher.DispatchAsync(
                        decodeResult.Message!,
                        cancellationToken);

                result =
                    dispatchResult.Outcome switch
                    {
                        CatalogOutboxDispatchOutcome.Success =>
                            await CompleteAsync(
                                message,
                                cancellationToken),

                        CatalogOutboxDispatchOutcome.TransientFailure =>
                            await RecordFailureAsync(
                                message,
                                CatalogOutboxFailureKind.Transient,
                                GetRequiredErrorCode(
                                    dispatchResult.ErrorCode),
                                cancellationToken),

                        CatalogOutboxDispatchOutcome.PermanentFailure =>
                            await RecordFailureAsync(
                                message,
                                CatalogOutboxFailureKind.Permanent,
                                GetRequiredErrorCode(
                                    dispatchResult.ErrorCode),
                                cancellationToken),

                        _ =>
                            throw new InvalidOperationException(
                                "The Catalog Outbox dispatcher returned an unsupported outcome.")
                    };
            }

            CatalogOutboxTelemetry.RecordCompletion(
                activityScope.Activity,
                result.Outcome,
                Stopwatch.GetElapsedTime(
                    startedTimestamp));

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            activityScope.Activity?.SetStatus(
                ActivityStatusCode.Error);

            throw;
        }
    }
    private async ValueTask<CatalogOutboxProcessResult>
        CompleteAsync(
            ClaimedCatalogOutboxMessage message,
            CancellationToken cancellationToken)
    {
        var updated =
            await _store.MarkProcessedAsync(
                message.Id,
                message.LeaseOwner,
                cancellationToken);

        return updated
            ? CatalogOutboxProcessResult.Processed
            : CatalogOutboxProcessResult.LeaseLost;
    }

    private async ValueTask<CatalogOutboxProcessResult>
        RecordFailureAsync(
            ClaimedCatalogOutboxMessage message,
            CatalogOutboxFailureKind failureKind,
            string errorCode,
            CancellationToken cancellationToken)
    {
        var failure =
            await _store.RecordFailureAsync(
                message,
                failureKind,
                errorCode,
                cancellationToken);

        if (!failure.Updated)
        {
            return CatalogOutboxProcessResult.LeaseLost;
        }

        if (failure.DeadLettered)
        {
            return CatalogOutboxProcessResult.DeadLettered(
                errorCode,
                failure.AttemptCount ??
                    throw new InvalidOperationException(
                        "Dead-lettered Outbox failure has no attempt count."));
        }

        return CatalogOutboxProcessResult.RetryScheduled(
            errorCode,
            failure.AttemptCount ??
                throw new InvalidOperationException(
                    "Retryable Outbox failure has no attempt count."),
            failure.NextAttemptAtUtc ??
                throw new InvalidOperationException(
                    "Retryable Outbox failure has no next-attempt timestamp."));
    }

    private static string GetRequiredErrorCode(
        string? errorCode)
    {
        if (string.IsNullOrWhiteSpace(
            errorCode))
        {
            throw new InvalidOperationException(
                "A failed Outbox operation must provide an error code.");
        }

        return errorCode;
    }
}
