using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Commerce.Api.IntegrationTests.Catalog;

public sealed class PublishProductObservabilityTests :
    IClassFixture<CommerceApiFixture>
{
    private const string InstrumentationName = "Commerce.Application";
    private const string CommandName = "PublishProductCommand";
    private const string CommandSpanName = "PublishProductCommand.execute";
    private const string CounterName = "commerce.application.command.executions";
    private const string DurationName = "commerce.application.command.duration";
    private const string TraceState = "ecp=publication-test";

    private static readonly DateTimeOffset SeedTime =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] MetricTagNames =
        ["command.name", "command.outcome"];

    private static readonly string[] MessageTypes =
        ["catalog.product-published.v1", "catalog.storefront-product-cache-invalidate.v1"];

    private readonly CommerceApiFixture _fixture;

    public PublishProductObservabilityTests(CommerceApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SuccessfulPublicationPersistsCommandTraceAndEmitsBoundedMetrics()
    {
        var product = await SeedAsync(withVariant: true);
        var traceId = ActivityTraceId.CreateRandom();
        var remoteSpanId = ActivitySpanId.CreateRandom();
        using var probe = new TelemetryProbe(traceId);
        using var client = _fixture.CreateClient(authenticated: true, authorized: true);
        var ambientBefore = Activity.Current;

        using var response = await SendWithTraceAsync(
            client, product.Id.Value, traceId, remoteSpanId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Same(ambientBefore, Activity.Current);

        var span = Assert.Single(probe.CommandSpans);
        AssertCommandTrace(span, traceId, remoteSpanId, "success", ActivityStatusCode.Ok);
        Assert.DoesNotContain(span.Tags, tag => tag.Key == "error.type");
        AssertMeasurements(probe.Measurements, "success", span.SpanId);

        var messages = await ReadOutboxAsync(product.Id.Value);
        Assert.Equal(MessageTypes, messages.Select(message => message.MessageType).ToArray());
        Assert.Equal(2, messages.Select(message => message.Id).Distinct().Count());
        foreach (var message in messages)
        {
            Assert.Equal(span.Id, message.TraceParent);
            Assert.NotNull(message.TraceParent);
            Assert.Equal(55, message.TraceParent.Length);
            Assert.Equal(TraceState, message.TraceState);
            Assert.True(ActivityContext.TryParse(
                message.TraceParent, message.TraceState, isRemote: true, out var context));
            Assert.Equal(traceId, context.TraceId);
            Assert.Equal(span.SpanId, context.SpanId);
            Assert.Equal(ActivityTraceFlags.Recorded, context.TraceFlags);
            Assert.True(context.IsRemote);
            Assert.Equal(0, message.Attempts);
            Assert.True(message.IsPending);
        }
    }

    [Fact]
    public async Task RejectedPublicationCorrelatesProblemAndEmitsFailureWithoutOutbox()
    {
        var product = await SeedAsync(withVariant: false);
        var traceId = ActivityTraceId.CreateRandom();
        var remoteSpanId = ActivitySpanId.CreateRandom();
        using var probe = new TelemetryProbe(traceId);
        using var client = _fixture.CreateClient(authenticated: true, authorized: true);
        var ambientBefore = Activity.Current;

        using var response = await SendWithTraceAsync(
            client, product.Id.Value, traceId, remoteSpanId);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Same(ambientBefore, Activity.Current);

        var span = Assert.Single(probe.CommandSpans);
        AssertCommandTrace(span, traceId, remoteSpanId, "failure", ActivityStatusCode.Error);
        Assert.Equal("Catalog.Product.NoPublishableVariants", GetTag(span.Tags, "error.type"));
        Assert.Equal("Catalog.Product.NoPublishableVariants", span.StatusDescription);
        AssertMeasurements(probe.Measurements, "failure", span.SpanId);

        using var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Catalog.Product.NoPublishableVariants",
            problem.RootElement.GetProperty("code").GetString());
        var responseTraceId = problem.RootElement.GetProperty("traceId").GetString();
        Assert.True(ActivityContext.TryParse(responseTraceId, TraceState, out var problemContext));
        Assert.Equal(traceId, problemContext.TraceId);
        Assert.NotNull(span.Parent);
        Assert.Equal(span.Parent.SpanId, problemContext.SpanId);
        Assert.Empty(await ReadOutboxAsync(product.Id.Value));

        await using var scope = _fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var persisted = await repository.GetByIdAsync(product.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(persisted);
        Assert.Equal(ProductStatus.Draft, persisted.Status);
        Assert.Null(persisted.PublishedAtUtc);
    }

    private static async Task<HttpResponseMessage> SendWithTraceAsync(
        HttpClient client, Guid productId, ActivityTraceId traceId, ActivitySpanId remoteSpanId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "/api/catalog/products/" + productId.ToString("D", CultureInfo.InvariantCulture) + "/publish");
        request.Headers.Add("traceparent",
            "00-" + traceId.ToHexString() + "-" + remoteSpanId.ToHexString() + "-01");
        request.Headers.Add("tracestate", TraceState);

        // This AsyncLocal change affects only this async flow, not other running tests.
        // Force the server to read the remote headers rather than inherit the test runner's Activity.
        var ambient = Activity.Current;
        try
        {
            Activity.Current = null;
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
        finally
        {
            Activity.Current = ambient;
        }
    }

    private static void AssertCommandTrace(
        CommandSpanSnapshot span, ActivityTraceId traceId, ActivitySpanId remoteSpanId,
        string outcome, ActivityStatusCode status)
    {
        Assert.Equal(CommandSpanName, span.OperationName);
        Assert.Equal(ActivityIdFormat.W3C, span.IdFormat);
        Assert.Equal(ActivityKind.Internal, span.Kind);
        Assert.Equal(traceId, span.TraceId);
        Assert.NotEqual(default(ActivitySpanId), span.SpanId);
        Assert.Equal(TraceState, span.TraceState);
        Assert.Equal(status, span.Status);
        Assert.Equal(CommandName, GetTag(span.Tags, "command.name"));
        Assert.Equal(outcome, GetTag(span.Tags, "command.outcome"));
        Assert.True(span.Duration >= TimeSpan.Zero);

        Assert.NotNull(span.Parent);
        Assert.Equal(ActivityKind.Server, span.Parent.Kind);
        Assert.Equal(traceId, span.Parent.TraceId);
        Assert.Equal(span.Parent.SpanId, span.ParentSpanId);
        Assert.Equal(remoteSpanId, span.Parent.ParentSpanId);
        Assert.NotEqual(remoteSpanId, span.SpanId);
        Assert.NotEqual(span.Parent.SpanId, span.SpanId);
    }

    private static void AssertMeasurements(
        MeasurementSnapshot[] measurements, string outcome, ActivitySpanId commandSpanId)
    {
        Assert.Equal(2, measurements.Length);
        var counter = Assert.Single(measurements, measurement => measurement.Name == CounterName);
        var duration = Assert.Single(measurements, measurement => measurement.Name == DurationName);
        Assert.Equal(typeof(Counter<long>), counter.InstrumentType);
        Assert.Equal(1d, counter.Value);
        Assert.Equal(typeof(Histogram<double>), duration.InstrumentType);
        Assert.Equal("s", duration.Unit);
        Assert.True(double.IsFinite(duration.Value));
        Assert.True(duration.Value >= 0);

        foreach (var measurement in measurements)
        {
            Assert.Equal(commandSpanId, measurement.ActiveSpanId);
            var keys = measurement.Tags.Select(tag => tag.Key).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(MetricTagNames, keys);
            Assert.Equal(CommandName, GetTag(measurement.Tags, "command.name"));
            Assert.Equal(outcome, GetTag(measurement.Tags, "command.outcome"));
        }
    }

    private static string GetTag(KeyValuePair<string, object?>[] tags, string name)
    {
        return Assert.IsType<string>(Assert.Single(tags, tag => tag.Key == name).Value);
    }

    private async Task<Product> SeedAsync(bool withVariant)
    {
        var suffix = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        var product = Product.CreateDraft(
            ProductName.Create("Publication Observability Product").Value,
            ProductSlug.Create("ecp11g3-observe-" + suffix).Value,
            ProductDescription.Empty, SeedTime);
        if (withVariant)
        {
            var result = product.AddVariant(
                Sku.Create("OBS-" + suffix).Value, VariantOptionCombination.Empty, SeedTime.AddMinutes(1));
            Assert.True(result.IsSuccess, result.Error?.Code);
        }

        await using var scope = _fixture.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ICatalogUnitOfWork>();
        repository.Add(product);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        Assert.Empty(await ReadOutboxAsync(product.Id.Value));
        return product;
    }

    private async Task<OutboxTraceSnapshot[]> ReadOutboxAsync(Guid productId)
    {
        var dataSource = _fixture.Services.GetRequiredService<NpgsqlDataSource>();
        await using var connection = await dataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, trace_parent, trace_state, attempt_count,
                processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL
            FROM catalog.outbox_messages
            WHERE (payload ->> 'productId')::uuid = @product_id
            ORDER BY type;
            """;
        command.Parameters.AddWithValue("product_id", productId);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var messages = new List<OutboxTraceSnapshot>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            messages.Add(new OutboxTraceSnapshot(
                reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4), reader.GetBoolean(5)));
        }
        return messages.ToArray();
    }

    private sealed class TelemetryProbe : IDisposable
    {
        private readonly ActivityTraceId _traceId;
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;
        private readonly ConcurrentQueue<CommandSpanSnapshot> _spans = new();
        private readonly ConcurrentQueue<MeasurementSnapshot> _measurements = new();

        public TelemetryProbe(ActivityTraceId traceId)
        {
            _traceId = traceId;
            _activityListener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name == InstrumentationName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = RecordSpan
            };
            _meterListener = new MeterListener
            {
                InstrumentPublished = static (instrument, listener) =>
                {
                    if (instrument.Meter.Name == InstrumentationName &&
                        instrument.Name is CounterName or DurationName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            _meterListener.SetMeasurementEventCallback<long>(
                (instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
            _meterListener.SetMeasurementEventCallback<double>(
                (instrument, value, tags, _) => RecordMeasurement(instrument, value, tags));
            ActivitySource.AddActivityListener(_activityListener);
            _meterListener.Start();
        }

        public CommandSpanSnapshot[] CommandSpans => _spans.ToArray();
        public MeasurementSnapshot[] Measurements => _measurements.ToArray();

        public void Dispose()
        {
            _meterListener.Dispose();
            _activityListener.Dispose();
        }

        private void RecordSpan(Activity activity)
        {
            if (activity.TraceId != _traceId || activity.OperationName != CommandSpanName)
            {
                return;
            }

            var parent = activity.Parent;
            _spans.Enqueue(new CommandSpanSnapshot(
                activity.Id, activity.OperationName, activity.IdFormat, activity.Kind,
                activity.TraceId, activity.SpanId, activity.ParentSpanId, activity.TraceStateString,
                activity.Status, activity.StatusDescription, activity.Duration, activity.TagObjects.ToArray(),
                parent is null ? null : new ParentSpanSnapshot(
                    parent.TraceId, parent.SpanId, parent.ParentSpanId, parent.Kind)));
        }

        private void RecordMeasurement(
            Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var activity = Activity.Current;
            if (activity is null || activity.TraceId != _traceId || activity.OperationName != CommandSpanName)
            {
                return;
            }

            // TraceId is only a private test correlation filter, never a production metric dimension.
            // Copy tags while the callback is active; never keep the incoming span or mutate telemetry.
            _measurements.Enqueue(new MeasurementSnapshot(
                instrument.Name, instrument.GetType(), instrument.Unit, value, activity.SpanId, tags.ToArray()));
        }
    }

    private sealed record OutboxTraceSnapshot(
        Guid Id, string MessageType, string? TraceParent, string? TraceState, int Attempts, bool IsPending);

    private sealed record ParentSpanSnapshot(
        ActivityTraceId TraceId, ActivitySpanId SpanId, ActivitySpanId ParentSpanId, ActivityKind Kind);

    private sealed record CommandSpanSnapshot(
        string? Id, string OperationName, ActivityIdFormat IdFormat, ActivityKind Kind,
        ActivityTraceId TraceId, ActivitySpanId SpanId, ActivitySpanId ParentSpanId, string? TraceState,
        ActivityStatusCode Status, string? StatusDescription, TimeSpan Duration,
        KeyValuePair<string, object?>[] Tags, ParentSpanSnapshot? Parent);

    private sealed record MeasurementSnapshot(
        string Name, Type InstrumentType, string? Unit, double Value,
        ActivitySpanId ActiveSpanId, KeyValuePair<string, object?>[] Tags);
}
