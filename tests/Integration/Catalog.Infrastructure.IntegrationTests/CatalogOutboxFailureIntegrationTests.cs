using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxFailureIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly DateTimeOffset OccurredAtUtc =
        new(
            2026,
            8,
            11,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public CatalogOutboxFailureIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        TransientFailureSchedulesRetryAndReleasesLease()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        await InsertPendingMessageAsync(
            dataSource,
            messageId,
            0,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var firstClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var failure =
            await store.RecordFailureAsync(
                firstClaim,
                CatalogOutboxFailureKind.Transient,
                "catalog.outbox.dispatch-transient",
                TestContext.Current.CancellationToken);

        Assert.True(
            failure.Updated);

        Assert.False(
            failure.DeadLettered);

        Assert.Equal(
            1,
            failure.AttemptCount);

        Assert.NotNull(
            failure.NextAttemptAtUtc);

        var immediateClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            immediateClaim);

        var retryState =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            retryState.AttemptCount);

        Assert.False(
            retryState.DeadLettered);

        Assert.True(
            retryState.LeaseCleared);

        Assert.True(
            retryState.RetryScheduledInFuture);

        Assert.Equal(
            "catalog.outbox.dispatch-transient",
            retryState.LastErrorCode);

        await MakeRetryEligibleAsync(
            dataSource,
            messageId,
            TestContext.Current.CancellationToken);

        var secondClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-b",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            messageId,
            secondClaim.Id);

        Assert.Equal(
            1,
            secondClaim.AttemptCount);

        Assert.NotEqual(
            firstClaim.LeaseOwner,
            secondClaim.LeaseOwner);

        Assert.True(
            await store.MarkProcessedAsync(
                secondClaim.Id,
                secondClaim.LeaseOwner,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task
        PermanentFailureDeadLettersImmediately()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        await InsertPendingMessageAsync(
            dataSource,
            messageId,
            0,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var failure =
            await store.RecordFailureAsync(
                claim,
                CatalogOutboxFailureKind.Permanent,
                "catalog.outbox.invalid-payload",
                TestContext.Current.CancellationToken);

        Assert.True(
            failure.Updated);

        Assert.True(
            failure.DeadLettered);

        Assert.Equal(
            1,
            failure.AttemptCount);

        Assert.Null(
            failure.NextAttemptAtUtc);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            state.AttemptCount);

        Assert.True(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        Assert.Equal(
            "catalog.outbox.invalid-payload",
            state.LastErrorCode);

        var laterClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            laterClaim);
    }

    [Fact]
    public async Task
        FifthTransientFailureExhaustsRetryBudget()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        await InsertPendingMessageAsync(
            dataSource,
            messageId,
            CatalogOutboxRetryPolicy.MaximumAttempts - 1,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var claim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            CatalogOutboxRetryPolicy.MaximumAttempts - 1,
            claim.AttemptCount);

        var failure =
            await store.RecordFailureAsync(
                claim,
                CatalogOutboxFailureKind.Transient,
                "catalog.outbox.transport-unavailable",
                TestContext.Current.CancellationToken);

        Assert.True(
            failure.Updated);

        Assert.True(
            failure.DeadLettered);

        Assert.Equal(
            CatalogOutboxRetryPolicy.MaximumAttempts,
            failure.AttemptCount);

        Assert.Null(
            failure.NextAttemptAtUtc);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            CatalogOutboxRetryPolicy.MaximumAttempts,
            state.AttemptCount);

        Assert.True(
            state.DeadLettered);

        Assert.True(
            state.LeaseCleared);

        var laterClaim =
            await store.ClaimPendingAsync(
                "worker-b",
                1,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            laterClaim);
    }

    [Fact]
    public async Task
        StaleLeaseCannotRecordFailure()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await ResetOutboxAsync(
            dataSource,
            TestContext.Current.CancellationToken);

        var messageId =
            Guid.CreateVersion7();

        await InsertPendingMessageAsync(
            dataSource,
            messageId,
            0,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var firstClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        await ExpireLeaseAsync(
            dataSource,
            messageId,
            firstClaim.LeaseOwner,
            TestContext.Current.CancellationToken);

        var secondClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-b",
                    1,
                    TimeSpan.FromMinutes(1),
                    TestContext.Current.CancellationToken));

        var staleFailure =
            await store.RecordFailureAsync(
                firstClaim,
                CatalogOutboxFailureKind.Transient,
                "catalog.outbox.stale-worker",
                TestContext.Current.CancellationToken);

        Assert.False(
            staleFailure.Updated);

        Assert.False(
            staleFailure.DeadLettered);

        Assert.Null(
            staleFailure.AttemptCount);

        Assert.Null(
            staleFailure.NextAttemptAtUtc);

        var state =
            await ReadStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            0,
            state.AttemptCount);

        Assert.False(
            state.DeadLettered);

        Assert.False(
            state.LeaseCleared);

        Assert.Equal(
            secondClaim.LeaseOwner,
            state.LeaseOwner);

        Assert.Null(
            state.LastErrorCode);

        Assert.True(
            await store.MarkProcessedAsync(
                secondClaim.Id,
                secondClaim.LeaseOwner,
                TestContext.Current.CancellationToken));
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

    private static async Task InsertPendingMessageAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        int attemptCount,
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
                    attempt_count
                )
                VALUES (
                    @id,
                    'catalog.test.v1',
                    '{}'::jsonb,
                    @occurred_at_utc,
                    @attempt_count
                );
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            OccurredAtUtc);

        command.Parameters.AddWithValue(
            "attempt_count",
            attemptCount);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        Assert.Equal(
            1,
            affectedRows);
    }

    private static async Task MakeRetryEligibleAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                UPDATE catalog.outbox_messages
                SET next_attempt_at_utc =
                    GREATEST(
                        enqueued_at_utc,
                        CURRENT_TIMESTAMP -
                            INTERVAL '1 second')
                WHERE id = @id;
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(
                cancellationToken));
    }

    private static async Task ExpireLeaseAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                UPDATE catalog.outbox_messages
                SET locked_until_utc =
                    CURRENT_TIMESTAMP -
                    INTERVAL '1 second'
                WHERE id = @id
                  AND lock_owner = @lease_owner;
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        command.Parameters.AddWithValue(
            "lease_owner",
            leaseOwner);

        Assert.Equal(
            1,
            await command.ExecuteNonQueryAsync(
                cancellationToken));
    }

    private static async Task<OutboxState>
        ReadStateAsync(
            NpgsqlDataSource dataSource,
            Guid messageId,
            CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                SELECT
                    attempt_count,
                    dead_lettered_at_utc IS NOT NULL,
                    lock_owner IS NULL
                        AND locked_until_utc IS NULL,
                    next_attempt_at_utc >
                        CURRENT_TIMESTAMP,
                    last_error_code,
                    lock_owner
                FROM catalog.outbox_messages
                WHERE id = @id;
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        Assert.True(
            await reader.ReadAsync(
                cancellationToken));

        var state =
            new OutboxState(
                reader.GetInt32(0),
                reader.GetBoolean(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),
                reader.IsDBNull(5)
                    ? null
                    : reader.GetString(5));

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));

        return state;
    }

    private sealed record OutboxState(
        int AttemptCount,
        bool DeadLettered,
        bool LeaseCleared,
        bool RetryScheduledInFuture,
        string? LastErrorCode,
        string? LeaseOwner);
}
