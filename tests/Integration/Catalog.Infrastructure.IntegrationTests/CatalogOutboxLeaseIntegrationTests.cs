using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxLeaseIntegrationTests :
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

    public CatalogOutboxLeaseIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        ExpiredLeaseCanBeReclaimedAndStaleClaimCannotComplete()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        var messageId =
            Guid.CreateVersion7();

        await InsertPendingMessageAsync(
            dataSource,
            messageId,
            TestContext.Current.CancellationToken);

        var store =
            new CatalogOutboxStore(
                dataSource);

        var firstClaim =
            Assert.Single(
                await store.ClaimPendingAsync(
                    "worker-a",
                    1,
                    TimeSpan.FromMinutes(5),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            messageId,
            firstClaim.Id);

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
                    TimeSpan.FromMinutes(5),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            messageId,
            secondClaim.Id);

        Assert.NotEqual(
            firstClaim.LeaseOwner,
            secondClaim.LeaseOwner);

        var staleCompletion =
            await store.MarkProcessedAsync(
                messageId,
                firstClaim.LeaseOwner,
                TestContext.Current.CancellationToken);

        Assert.False(
            staleCompletion);

        var currentCompletion =
            await store.MarkProcessedAsync(
                messageId,
                secondClaim.LeaseOwner,
                TestContext.Current.CancellationToken);

        Assert.True(
            currentCompletion);

        var repeatedCompletion =
            await store.MarkProcessedAsync(
                messageId,
                secondClaim.LeaseOwner,
                TestContext.Current.CancellationToken);

        Assert.False(
            repeatedCompletion);

        var thirdClaim =
            await store.ClaimPendingAsync(
                "worker-c",
                1,
                TimeSpan.FromMinutes(5),
                TestContext.Current.CancellationToken);

        Assert.Empty(
            thirdClaim);

        var finalState =
            await ReadFinalStateAsync(
                dataSource,
                messageId,
                TestContext.Current.CancellationToken);

        Assert.True(
            finalState.Processed);

        Assert.True(
            finalState.LeaseCleared);
    }

    private static async Task InsertPendingMessageAsync(
        NpgsqlDataSource dataSource,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                INSERT INTO catalog.outbox_messages (
                    id,
                    type,
                    payload,
                    occurred_at_utc
                )
                VALUES (
                    @id,
                    'catalog.test.v1',
                    '{}'::jsonb,
                    @occurred_at_utc
                );
                """);

        command.Parameters.AddWithValue(
            "id",
            messageId);

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            OccurredAtUtc);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        Assert.Equal(
            1,
            affectedRows);
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

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        Assert.Equal(
            1,
            affectedRows);
    }

    private static async Task<OutboxFinalState>
        ReadFinalStateAsync(
            NpgsqlDataSource dataSource,
            Guid messageId,
            CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                SELECT
                    processed_at_utc IS NOT NULL,
                    lock_owner IS NULL
                        AND locked_until_utc IS NULL
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
            new OutboxFinalState(
                reader.GetBoolean(0),
                reader.GetBoolean(1));

        Assert.False(
            await reader.ReadAsync(
                cancellationToken));

        return state;
    }

    private sealed record OutboxFinalState(
        bool Processed,
        bool LeaseCleared);
}
