using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxClaimIntegrationTests :
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

    public CatalogOutboxClaimIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConcurrentWorkersClaimDisjointMessages()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        var messageIds =
            Enumerable
                .Range(0, 6)
                .Select(_ => Guid.CreateVersion7())
                .ToArray();

        await InsertPendingMessagesAsync(
            dataSource,
            messageIds,
            TestContext.Current.CancellationToken);

        var firstStore =
            new CatalogOutboxStore(dataSource);

        var secondStore =
            new CatalogOutboxStore(dataSource);

        var firstClaim =
            firstStore.ClaimPendingAsync(
                "worker-a",
                3,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        var secondClaim =
            secondStore.ClaimPendingAsync(
                "worker-b",
                3,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        var claims =
            await Task.WhenAll(
                firstClaim,
                secondClaim);

        Assert.Equal(
            3,
            claims[0].Length);

        Assert.Equal(
            3,
            claims[1].Length);

        var allClaimedIds =
            claims
                .SelectMany(static claim => claim)
                .Select(static message => message.Id)
                .ToArray();

        Assert.Equal(
            6,
            allClaimedIds.Length);

        Assert.Equal(
            6,
            allClaimedIds
                .Distinct()
                .Count());

        Assert.Equal(
            messageIds
                .Order()
                .ToArray(),
            allClaimedIds
                .Order()
                .ToArray());

        var thirdClaim =
            await firstStore.ClaimPendingAsync(
                "worker-c",
                10,
                TimeSpan.FromMinutes(1),
                TestContext.Current.CancellationToken);

        Assert.Empty(thirdClaim);

        var activeLeaseCount =
            await CountActiveLeasesAsync(
                dataSource,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            6L,
            activeLeaseCount);
    }

    private static async Task InsertPendingMessagesAsync(
        NpgsqlDataSource dataSource,
        IEnumerable<Guid> messageIds,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await dataSource.OpenConnectionAsync(
                cancellationToken);

        foreach (var messageId in messageIds)
        {
            await using var command =
                connection.CreateCommand();

            command.CommandText =
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
                """;

            command.Parameters.AddWithValue(
                "id",
                messageId);

            command.Parameters.AddWithValue(
                "occurred_at_utc",
                OccurredAtUtc);

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }
    }

    private static async Task<long> CountActiveLeasesAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        await using var command =
            dataSource.CreateCommand(
                """
                SELECT COUNT(*)
                FROM catalog.outbox_messages
                WHERE lock_owner IS NOT NULL
                  AND locked_until_utc >
                      CURRENT_TIMESTAMP;
                """);

        var result =
            await command.ExecuteScalarAsync(
                cancellationToken);

        return (long)(
            result ??
            throw new InvalidOperationException(
                "The active lease count was not returned."));
    }
}
