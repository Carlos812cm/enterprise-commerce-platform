using System.Diagnostics;
using System.Text.Json;
using Catalog.Application.Abstractions.Caching;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxProcessorTracingIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private const string TraceParent =
        "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";

    private const string TraceState =
        "vendor=value";

    private static readonly Guid ProductId =
        Guid.Parse(
            "019c2b42-3e1e-782f-b117-891e68dc89eb");

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);
    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxProcessorTracingIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        ProcessingRestoresStoredTraceDuringDispatchAndRestoresAmbientContextAfterward()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        using var listener =
            new ActivityListener
            {
                ShouldListenTo =
                    source =>
                        source.Name ==
                        CatalogOutboxActivity
                            .ActivitySourceName,

                Sample =
                    static (
                        ref ActivityCreationOptions<ActivityContext> _) =>
                            ActivitySamplingResult
                                .AllDataAndRecorded
            };

        ActivitySource.AddActivityListener(
            listener);

        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            cancellationToken);

        var messageId =
            Guid.CreateVersion7();

        var payload =
            JsonSerializer.Serialize(
                new StorefrontProductCacheInvalidationV1(
                    ProductId,
                    "traceable-monitor",
                    DateTimeOffset.UtcNow),
                SerializerOptions);

        await InsertMessageAsync(
            dataSource,
            messageId,
            payload,
            cancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claimed =
            await store.ClaimPendingAsync(
                "worker-tracing",
                batchSize: 1,
                leaseDuration:
                    TimeSpan.FromMinutes(1),
                cancellationToken);

        var message =
            Assert.Single(
                claimed);

        Assert.Equal(
            messageId,
            message.Id);

        Assert.Equal(
            TraceParent,
            message.TraceParent);

        Assert.Equal(
            TraceState,
            message.TraceState);

        var invalidator =
            new ActivityCapturingCacheInvalidator();

        var dispatcher =
            new CatalogOutboxDispatcher(
                invalidator,
                new NoOpStorefrontProductCacheInvalidationBroadcaster(),
                new UnexpectedPublisher());

        var processor =
            new CatalogOutboxMessageProcessor(
                store,
                dispatcher);

        using var ambientActivity =
            new Activity(
                "ambient-test")
                .SetIdFormat(
                    ActivityIdFormat.W3C)
                .Start();

        Assert.NotNull(
            ambientActivity);

        var ambientTraceId =
            ambientActivity.TraceId;

        var result =
            await processor.ProcessAsync(
                message,
                cancellationToken);

        Assert.Equal(
            CatalogOutboxProcessOutcome.Processed,
            result.Outcome);

        Assert.Equal(
            "4bf92f3577b34da6a3ce929d0e0e4736",
            invalidator.TraceId.ToString());

        Assert.Equal(
            "00f067aa0ba902b7",
            invalidator.ParentSpanId.ToString());

        Assert.Equal(
            CatalogOutboxActivity
                .ProcessActivityName,
            invalidator.OperationName);

        Assert.Equal(
            TraceState,
            invalidator.TraceState);

        Assert.NotEqual(
            ambientTraceId,
            invalidator.TraceId);

        Assert.Same(
            ambientActivity,
            Activity.Current);

        var processed =
            await IsProcessedAsync(
                dataSource,
                messageId,
                cancellationToken);

        Assert.True(
            processed);
    }

    private static async Task ResetOutboxAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                TRUNCATE TABLE
                    catalog.outbox_messages;
                """);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    private static async Task InsertMessageAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                INSERT INTO catalog.outbox_messages (
                    id,
                    type,
                    payload,
                    occurred_at_utc,
                    trace_parent,
                    trace_state
                )
                VALUES (
                    @id,
                    @type,
                    CAST(@payload AS jsonb),
                    CURRENT_TIMESTAMP,
                    @trace_parent,
                    @trace_state
                );
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        command.Parameters.AddWithValue(
            "type",
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1);

        command.Parameters.AddWithValue(
            "payload",
            payload);

        command.Parameters.AddWithValue(
            "trace_parent",
            TraceParent);

        command.Parameters.AddWithValue(
            "trace_state",
            TraceState);

        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(
                cancellationToken));
    }

    private static async Task<bool> IsProcessedAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                SELECT
                    processed_at_utc IS NOT NULL
                FROM catalog.outbox_messages
                WHERE id = @id;
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return Assert.IsType<bool>(
            result);
    }

    private sealed class ActivityCapturingCacheInvalidator :
        IStorefrontProductCacheInvalidator
    {
        public ActivityTraceId TraceId { get; private set; }

        public ActivitySpanId ParentSpanId { get; private set; }

        public string? OperationName { get; private set; }

        public string? TraceState { get; private set; }

        public ValueTask InvalidateBySlugAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(
                slug);

            cancellationToken
                .ThrowIfCancellationRequested();

            var activity =
                Activity.Current;

            Assert.NotNull(
                activity);

            TraceId =
                activity.TraceId;

            ParentSpanId =
                activity.ParentSpanId;

            OperationName =
                activity.OperationName;

            TraceState =
                activity.TraceStateString;

            return ValueTask.CompletedTask;
        }

        public ValueTask InvalidateAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new NotSupportedException(
                "InvalidateAllAsync is not used by this test.");
        }
    }

    private sealed class UnexpectedPublisher :
        ICatalogProductPublishedPublisher
    {
        public ValueTask<CatalogOutboxDispatchResult>
            PublishAsync(
                Guid outboxMessageId,
                ProductPublishedIntegrationEventV1 integrationEvent,
                CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            throw new InvalidOperationException(
                "ProductPublished publisher must not be called by this test.");
        }
    }
}
