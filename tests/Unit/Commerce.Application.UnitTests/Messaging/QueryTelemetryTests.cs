using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Commerce.Application.DependencyInjection;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Commerce.Application.UnitTests.Messaging;

public sealed class QueryTelemetryTests
{
    [Fact]
    public async Task DispatchEmitsActivityAndMetrics()
    {
        Activity? stoppedActivity = null;

        using var activityListener =
            new ActivityListener
            {
                ShouldListenTo = static source =>
                    source.Name == "Commerce.Application",

                Sample = static (
                    ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,

                SampleUsingParentId = static (
                    ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,

                ActivityStopped = activity =>
                {
                    if (object.Equals(
                            activity.GetTagItem("query.name"),
                            nameof(TelemetryQuery)))
                    {
                        stoppedActivity = activity;
                    }
                }
            };

        ActivitySource.AddActivityListener(
            activityListener);

        var metricRecords =
            new ConcurrentQueue<MetricRecord>();

        using var meterListener =
            new MeterListener();

        meterListener.InstrumentPublished =
            (instrument, listener) =>
            {
                if (instrument.Meter.Name ==
                    "Commerce.Application")
                {
                    listener.EnableMeasurementEvents(
                        instrument);
                }
            };

        meterListener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) =>
            {
                metricRecords.Enqueue(
                    new MetricRecord(
                        instrument.Name,
                        measurement,
                        GetTag(tags, "query.name"),
                        GetTag(tags, "query.outcome")));
            });

        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) =>
            {
                metricRecords.Enqueue(
                    new MetricRecord(
                        instrument.Name,
                        measurement,
                        GetTag(tags, "query.name"),
                        GetTag(tags, "query.outcome")));
            });

        meterListener.Start();

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddQueryHandler<
            TelemetryQuery,
            TelemetryResponse,
            TelemetryQueryHandler>();

        using var serviceProvider =
            services.BuildServiceProvider();

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new TelemetryQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(stoppedActivity);

        Assert.Equal(
            ActivityStatusCode.Ok,
            stoppedActivity.Status);

        Assert.Equal(
            "success",
            stoppedActivity.GetTagItem(
                "query.outcome"));

        Assert.Contains(
            metricRecords,
            record =>
                record.InstrumentName ==
                "commerce.application.query.executions" &&
                record.QueryName ==
                nameof(TelemetryQuery) &&
                record.Outcome == "success");

        Assert.Contains(
            metricRecords,
            record =>
                record.InstrumentName ==
                "commerce.application.query.duration" &&
                record.QueryName ==
                nameof(TelemetryQuery) &&
                record.Value >= 0);
    }

    private static string? GetTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string name)
    {
        foreach (var tag in tags)
        {
            if (string.Equals(
                    tag.Key,
                    name,
                    StringComparison.Ordinal))
            {
                return tag.Value?.ToString();
            }
        }

        return null;
    }

    private sealed record TelemetryQuery :
        Query<TelemetryResponse>;

    private sealed record TelemetryResponse(
        string Value);

    private sealed class TelemetryQueryHandler :
        IQueryHandler<TelemetryQuery, TelemetryResponse>
    {
        public Task<Result<TelemetryResponse>> HandleAsync(
            TelemetryQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Result.Success(
                    new TelemetryResponse("observed")));
        }
    }

    private sealed record MetricRecord(
        string InstrumentName,
        double Value,
        string? QueryName,
        string? Outcome);
}
