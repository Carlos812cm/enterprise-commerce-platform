using System.Diagnostics;
using System.Diagnostics.Metrics;
using Commerce.Application.Diagnostics;
using Commerce.Domain;

namespace Commerce.Application.Messaging.Behaviors;

internal sealed class CommandTelemetryBehavior<TCommand, TResponse> :
    ICommandBehavior<TCommand, TResponse>
    where TCommand : Command<TResponse>
{
    public int Order =>
        CommandBehaviorOrder.Telemetry;

    public async Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CommandHandlerContinuation<TResponse> handlerContinuation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(handlerContinuation);

        var commandName = typeof(TCommand).Name;
        var startedTimestamp = Stopwatch.GetTimestamp();
        var outcome = "error";

        using var activity =
            ApplicationDiagnostics.ActivitySource.StartActivity(
                $"{commandName}.execute",
                ActivityKind.Internal);

        activity?.SetTag(
            "command.name",
            commandName);

        try
        {
            var result = await handlerContinuation(cancellationToken)
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
                "Command execution was cancelled.");

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
                "command.outcome",
                outcome);

            var tags = new TagList
            {
                { "command.name", commandName },
                { "command.outcome", outcome }
            };

            ApplicationDiagnostics.CommandExecutions.Add(
                1,
                tags);

            ApplicationDiagnostics.CommandDuration.Record(
                Stopwatch
                    .GetElapsedTime(startedTimestamp)
                    .TotalSeconds,
                tags);
        }
    }
}
