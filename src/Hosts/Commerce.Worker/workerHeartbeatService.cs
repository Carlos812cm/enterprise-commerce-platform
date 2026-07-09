using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Commerce.Worker;

internal sealed partial class WorkerHeartbeatService(
    ILogger<WorkerHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogWorkerStarted(logger);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogWorkerStopping(logger);
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Commerce worker process started.")]
    private static partial void LogWorkerStarted(ILogger logger);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Commerce worker process is stopping.")]
    private static partial void LogWorkerStopping(ILogger logger);
}