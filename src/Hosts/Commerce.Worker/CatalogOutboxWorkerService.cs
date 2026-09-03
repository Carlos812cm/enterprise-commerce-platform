using System.Globalization;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Commerce.Worker;

internal sealed partial class CatalogOutboxWorkerService :
    BackgroundService
{
    private const int MaximumMachineNameLength =
        48;

    private readonly ICatalogOutboxBatchProcessor
        _batchProcessor;

    private readonly CatalogOutboxWorkerOptions
        _options;

    private readonly ILogger<CatalogOutboxWorkerService>
        _logger;

    private readonly string _workerId;

    public CatalogOutboxWorkerService(
        ICatalogOutboxBatchProcessor batchProcessor,
        IOptions<CatalogOutboxWorkerOptions> options,
        ILogger<CatalogOutboxWorkerService> logger)
    {
        ArgumentNullException.ThrowIfNull(
            batchProcessor);

        ArgumentNullException.ThrowIfNull(
            options);

        ArgumentNullException.ThrowIfNull(
            logger);

        _batchProcessor =
            batchProcessor;

        _options =
            options.Value;

        _logger =
            logger;

        _workerId =
            CreateWorkerId();
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        LogWorkerStarted(
            _logger,
            _options.BatchSize,
            _options.LeaseDuration.TotalSeconds,
            _options.IdleDelay.TotalMilliseconds);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var result =
                    await _batchProcessor.RunAsync(
                        _workerId,
                        _options.BatchSize,
                        _options.LeaseDuration,
                        stoppingToken);

                if (result.HasWork)
                {
                    LogBatchResult(
                        result);

                    continue;
                }

                await Task.Delay(
                    _options.IdleDelay,
                    stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            LogWorkerStopped(
                _logger);
        }
    }

    private void LogBatchResult(
        CatalogOutboxBatchResult result)
    {
        if (
            result.RetryScheduledCount > 0 ||
            result.DeadLetteredCount > 0 ||
            result.LeaseLostCount > 0)
        {
            LogBatchWithNonSuccessOutcomes(
                _logger,
                result.ClaimedCount,
                result.ProcessedCount,
                result.RetryScheduledCount,
                result.DeadLetteredCount,
                result.LeaseLostCount);

            return;
        }

        LogBatchCompleted(
            _logger,
            result.ClaimedCount,
            result.ProcessedCount);
    }

    private static string CreateWorkerId()
    {
        var machineName =
            Environment.MachineName;

        var boundedMachineName =
            machineName.Length <=
                MaximumMachineNameLength
                ? machineName
                : machineName[
                    ..MaximumMachineNameLength];

        return string.Concat(
            "commerce-worker:",
            boundedMachineName,
            ":",
            Environment.ProcessId.ToString(
                CultureInfo.InvariantCulture));
    }

    [LoggerMessage(
        EventId = 1100,
        Level = LogLevel.Information,
        Message =
            "Catalog Outbox worker started. BatchSize={BatchSize}, LeaseDurationSeconds={LeaseDurationSeconds}, IdleDelayMilliseconds={IdleDelayMilliseconds}.")]
    private static partial void LogWorkerStarted(
        ILogger logger,
        int batchSize,
        double leaseDurationSeconds,
        double idleDelayMilliseconds);

    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Debug,
        Message =
            "Catalog Outbox batch completed successfully. Claimed={ClaimedCount}, Processed={ProcessedCount}.")]
    private static partial void LogBatchCompleted(
        ILogger logger,
        int claimedCount,
        int processedCount);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Warning,
        Message =
            "Catalog Outbox batch completed with non-success outcomes. Claimed={ClaimedCount}, Processed={ProcessedCount}, RetryScheduled={RetryScheduledCount}, DeadLettered={DeadLetteredCount}, LeaseLost={LeaseLostCount}.")]
    private static partial void
        LogBatchWithNonSuccessOutcomes(
            ILogger logger,
            int claimedCount,
            int processedCount,
            int retryScheduledCount,
            int deadLetteredCount,
            int leaseLostCount);

    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Information,
        Message =
            "Catalog Outbox worker stopped.")]
    private static partial void LogWorkerStopped(
        ILogger logger);
}
