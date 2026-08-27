using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class OutboxSchemaIntegrationTests :
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

    private static readonly DateTimeOffset EnqueuedAtUtc =
        OccurredAtUtc.AddSeconds(1);

    private readonly CatalogPostgreSqlFixture _fixture;

    public OutboxSchemaIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MigrationCreatesExpectedOutboxSchema()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                to_regclass(
                    'catalog.outbox_messages')
                    IS NOT NULL,

                EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'catalog'
                      AND table_name = 'outbox_messages'
                      AND column_name = 'payload'
                      AND data_type = 'jsonb'
                ),

                EXISTS (
                    SELECT 1
                    FROM pg_indexes
                    WHERE schemaname = 'catalog'
                      AND tablename = 'outbox_messages'
                      AND indexname =
                          'ix_outbox_messages_pending'
                      AND indexdef LIKE
                          '%processed_at_utc IS NULL%'
                      AND indexdef LIKE
                          '%dead_lettered_at_utc IS NULL%'
                ),

                (
                    SELECT COUNT(*)
                    FROM pg_constraint constraint_record
                    INNER JOIN pg_class table_record
                        ON table_record.oid =
                            constraint_record.conrelid
                    INNER JOIN pg_namespace schema_record
                        ON schema_record.oid =
                            table_record.relnamespace
                    WHERE schema_record.nspname = 'catalog'
                      AND table_record.relname =
                          'outbox_messages'
                      AND constraint_record.conname IN (
                          'ck_outbox_messages_attempt_count',
                          'ck_outbox_messages_lease_pair',
                          'ck_outbox_messages_next_attempt',
                          'ck_outbox_messages_terminal_lease',
                          'ck_outbox_messages_terminal_state'
                      )
                ) = 5,

                EXISTS (
                    SELECT 1
                    FROM catalog.__ef_migrations_history
                    WHERE "MigrationId" =
                        '20260811051042_AddCatalogOutbox'
                );
            """;

        await using var reader =
            await command.ExecuteReaderAsync(
                TestContext.Current.CancellationToken);

        Assert.True(
            await reader.ReadAsync(
                TestContext.Current.CancellationToken));

        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));

        Assert.False(
            await reader.ReadAsync(
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PendingMessageUsesDatabaseDefaults()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

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
                @type,
                CAST(@payload AS jsonb),
                @occurred_at_utc
            )
            RETURNING
                attempt_count,
                enqueued_at_utc IS NOT NULL,
                next_attempt_at_utc >= enqueued_at_utc,
                lock_owner IS NULL
                    AND locked_until_utc IS NULL,
                processed_at_utc IS NULL,
                dead_lettered_at_utc IS NULL;
            """;

        command.Parameters.AddWithValue(
            "id",
            Guid.CreateVersion7());

        command.Parameters.AddWithValue(
            "type",
            "catalog.test.v1");

        command.Parameters.AddWithValue(
            "payload",
            """{"productId":"test"}""");

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            OccurredAtUtc);

        await using var reader =
            await command.ExecuteReaderAsync(
                TestContext.Current.CancellationToken);

        Assert.True(
            await reader.ReadAsync(
                TestContext.Current.CancellationToken));

        Assert.Equal(
            0,
            reader.GetInt32(0));

        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
        Assert.True(reader.GetBoolean(5));
    }

    [Fact]
    public async Task NegativeAttemptCountIsRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc,
                enqueued_at_utc,
                attempt_count,
                next_attempt_at_utc
            )
            VALUES (
                @id,
                'catalog.test.v1',
                '{}'::jsonb,
                @occurred_at_utc,
                @enqueued_at_utc,
                -1,
                @enqueued_at_utc
            );
            """;

        AddCommonParameters(command);

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState);

        Assert.Equal(
            "ck_outbox_messages_attempt_count",
            exception.ConstraintName);
    }

    [Fact]
    public async Task PartialLeaseIsRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc,
                enqueued_at_utc,
                next_attempt_at_utc,
                lock_owner
            )
            VALUES (
                @id,
                'catalog.test.v1',
                '{}'::jsonb,
                @occurred_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc,
                'worker-1'
            );
            """;

        AddCommonParameters(command);

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState);

        Assert.Equal(
            "ck_outbox_messages_lease_pair",
            exception.ConstraintName);
    }

    [Fact]
    public async Task ProcessedAndDeadLetteredMessageIsRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc,
                enqueued_at_utc,
                next_attempt_at_utc,
                processed_at_utc,
                dead_lettered_at_utc
            )
            VALUES (
                @id,
                'catalog.test.v1',
                '{}'::jsonb,
                @occurred_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc
            );
            """;

        AddCommonParameters(command);

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState);

        Assert.Equal(
            "ck_outbox_messages_terminal_state",
            exception.ConstraintName);
    }

    [Fact]
    public async Task TerminalMessageWithLeaseIsRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc,
                enqueued_at_utc,
                next_attempt_at_utc,
                lock_owner,
                locked_until_utc,
                processed_at_utc
            )
            VALUES (
                @id,
                'catalog.test.v1',
                '{}'::jsonb,
                @occurred_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc,
                'worker-1',
                @locked_until_utc,
                @enqueued_at_utc
            );
            """;

        AddCommonParameters(command);

        command.Parameters.AddWithValue(
            "locked_until_utc",
            EnqueuedAtUtc.AddMinutes(1));

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState);

        Assert.Equal(
            "ck_outbox_messages_terminal_lease",
            exception.ConstraintName);
    }

    [Fact]
    public async Task RetryBeforeEnqueueIsRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var dataSource =
            serviceProvider.GetRequiredService<
                NpgsqlDataSource>();

        await using var connection =
            await dataSource.OpenConnectionAsync(
                TestContext.Current.CancellationToken);

        await using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO catalog.outbox_messages (
                id,
                type,
                payload,
                occurred_at_utc,
                enqueued_at_utc,
                next_attempt_at_utc
            )
            VALUES (
                @id,
                'catalog.test.v1',
                '{}'::jsonb,
                @occurred_at_utc,
                @enqueued_at_utc,
                @enqueued_at_utc - INTERVAL '1 second'
            );
            """;

        AddCommonParameters(command);

        var exception =
            await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            exception.SqlState);

        Assert.Equal(
            "ck_outbox_messages_next_attempt",
            exception.ConstraintName);
    }

    private static void AddCommonParameters(
        NpgsqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Parameters.AddWithValue(
            "id",
            Guid.CreateVersion7());

        command.Parameters.AddWithValue(
            "occurred_at_utc",
            OccurredAtUtc);

        command.Parameters.AddWithValue(
            "enqueued_at_utc",
            EnqueuedAtUtc);
    }
}
