using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal sealed class CatalogOutboxStore
{
    private const int LockOwnerMaximumLength = 128;
    private const int LastErrorCodeMaximumLength = 128;
    private const int LeaseTokenLength = 32;
    private const int LeaseTokenSeparatorLength = 1;

    private const int WorkerIdMaximumLength =
        LockOwnerMaximumLength -
        LeaseTokenLength -
        LeaseTokenSeparatorLength;

    private readonly NpgsqlDataSource _dataSource;

    public CatalogOutboxStore(
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        _dataSource = dataSource;
    }

    public async Task<ClaimedCatalogOutboxMessage[]>
        ClaimPendingAsync(
            string workerId,
            int batchSize,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workerId);

        if (workerId.Length >
            WorkerIdMaximumLength)
        {
            throw new ArgumentException(
                "The worker identifier exceeds the supported length.",
                nameof(workerId));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            batchSize);

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                leaseDuration,
                "The lease duration must be positive.");
        }

        var leaseOwner =
            CreateLeaseOwner(workerId);

        await using var connection =
            await _dataSource.OpenConnectionAsync(
                cancellationToken);

        await using var transaction =
            await connection.BeginTransactionAsync(
                cancellationToken);

        await using var command =
            connection.CreateCommand();

        command.Transaction = transaction;

        command.CommandText =
            """
            WITH claimable AS (
                SELECT id
                FROM catalog.outbox_messages
                WHERE processed_at_utc IS NULL
                  AND dead_lettered_at_utc IS NULL
                  AND next_attempt_at_utc <=
                      CURRENT_TIMESTAMP
                  AND (
                      locked_until_utc IS NULL
                      OR locked_until_utc <=
                          CURRENT_TIMESTAMP
                  )
                ORDER BY
                    next_attempt_at_utc,
                    occurred_at_utc,
                    id
                FOR UPDATE SKIP LOCKED
                LIMIT @batch_size
            )
            UPDATE catalog.outbox_messages AS message
            SET
                lock_owner = @lease_owner,
                locked_until_utc =
                    CURRENT_TIMESTAMP +
                    @lease_duration
            FROM claimable
            WHERE message.id = claimable.id
            RETURNING
                message.id,
                message.type,
                message.payload,
                message.occurred_at_utc,
                message.enqueued_at_utc,
                message.attempt_count,
                message.lock_owner,
                message.locked_until_utc,
                message.trace_parent,
                message.trace_state;
            """;

        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            leaseOwner);

        command.Parameters.AddWithValue(
            "batch_size",
            NpgsqlDbType.Integer,
            batchSize);

        command.Parameters.AddWithValue(
            "lease_duration",
            NpgsqlDbType.Interval,
            leaseDuration);

        var claimedMessages =
            new List<ClaimedCatalogOutboxMessage>(
                batchSize);

        await using (var reader =
            await command.ExecuteReaderAsync(
                cancellationToken))
        {
            while (await reader.ReadAsync(
                cancellationToken))
            {
                claimedMessages.Add(
                    new ClaimedCatalogOutboxMessage(
                        reader.GetGuid(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetFieldValue<DateTimeOffset>(3),
                        reader.GetFieldValue<DateTimeOffset>(4),
                        reader.GetInt32(5),
                        reader.GetString(6),
                        reader.GetFieldValue<DateTimeOffset>(7),
                        reader.IsDBNull(8)
                            ? null
                            : reader.GetString(8),
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetString(9)));
            }
        }

        await transaction.CommitAsync(
            cancellationToken);

        return claimedMessages.ToArray();
    }

    public async Task<bool> MarkProcessedAsync(
        Guid messageId,
        string leaseOwner,
        CancellationToken cancellationToken)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "The message identifier cannot be empty.",
                nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            leaseOwner);

        if (leaseOwner.Length >
            LockOwnerMaximumLength)
        {
            throw new ArgumentException(
                "The lease owner exceeds the supported length.",
                nameof(leaseOwner));
        }

        await using var command =
            _dataSource.CreateCommand(
                """
                UPDATE catalog.outbox_messages
                SET
                    processed_at_utc =
                        CURRENT_TIMESTAMP,
                    lock_owner = NULL,
                    locked_until_utc = NULL,
                    last_error_code = NULL
                WHERE id = @id
                  AND lock_owner = @lease_owner
                  AND locked_until_utc >
                      CURRENT_TIMESTAMP
                  AND processed_at_utc IS NULL
                  AND dead_lettered_at_utc IS NULL;
                """);

        command.Parameters.AddWithValue(
            "id",
            NpgsqlDbType.Uuid,
            messageId);

        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            leaseOwner);

        var affectedRows =
            await command.ExecuteNonQueryAsync(
                cancellationToken);

        return affectedRows == 1;
    }

    public async Task<CatalogOutboxFailureResult>
        RecordFailureAsync(
            ClaimedCatalogOutboxMessage message,
            CatalogOutboxFailureKind failureKind,
            string errorCode,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        ValidateMessageId(
            message.Id);

        ValidateLeaseOwner(
            message.LeaseOwner);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode);

        if (errorCode.Length >
            LastErrorCodeMaximumLength)
        {
            throw new ArgumentException(
                "The error code exceeds the supported length.",
                nameof(errorCode));
        }

        if (!Enum.IsDefined(failureKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                failureKind,
                "The failure kind is not supported.");
        }

        var failedAttemptNumber =
            checked(
                message.AttemptCount + 1);

        var shouldDeadLetter =
            failureKind ==
                CatalogOutboxFailureKind.Permanent ||
            failedAttemptNumber >=
                CatalogOutboxRetryPolicy.MaximumAttempts;

        var retryDelay =
            shouldDeadLetter
                ? TimeSpan.Zero
                : CatalogOutboxRetryPolicy.GetDelay(
                    failedAttemptNumber);

        await using var command =
            _dataSource.CreateCommand(
                """
                UPDATE catalog.outbox_messages
                SET
                    attempt_count =
                        @failed_attempt_number,
                    next_attempt_at_utc =
                        CASE
                            WHEN @dead_letter
                                THEN next_attempt_at_utc
                            ELSE
                                CURRENT_TIMESTAMP +
                                @retry_delay
                        END,
                    dead_lettered_at_utc =
                        CASE
                            WHEN @dead_letter
                                THEN CURRENT_TIMESTAMP
                            ELSE NULL
                        END,
                    last_error_code =
                        @error_code,
                    lock_owner = NULL,
                    locked_until_utc = NULL
                WHERE id = @id
                  AND lock_owner =
                      @lease_owner
                  AND locked_until_utc >
                      CURRENT_TIMESTAMP
                  AND attempt_count =
                      @expected_attempt_count
                  AND processed_at_utc IS NULL
                  AND dead_lettered_at_utc IS NULL
                RETURNING
                    attempt_count,
                    dead_lettered_at_utc IS NOT NULL,
                    CASE
                        WHEN dead_lettered_at_utc IS NULL
                            THEN next_attempt_at_utc
                        ELSE NULL
                    END;
                """);

        command.Parameters.AddWithValue(
            "failed_attempt_number",
            NpgsqlDbType.Integer,
            failedAttemptNumber);

        command.Parameters.AddWithValue(
            "dead_letter",
            NpgsqlDbType.Boolean,
            shouldDeadLetter);

        command.Parameters.AddWithValue(
            "retry_delay",
            NpgsqlDbType.Interval,
            retryDelay);

        command.Parameters.AddWithValue(
            "error_code",
            NpgsqlDbType.Text,
            errorCode);

        command.Parameters.AddWithValue(
            "id",
            NpgsqlDbType.Uuid,
            message.Id);

        command.Parameters.AddWithValue(
            "lease_owner",
            NpgsqlDbType.Text,
            message.LeaseOwner);

        command.Parameters.AddWithValue(
            "expected_attempt_count",
            NpgsqlDbType.Integer,
            message.AttemptCount);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(
            cancellationToken))
        {
            return CatalogOutboxFailureResult.LeaseLost;
        }

        var attemptCount =
            reader.GetInt32(0);

        var deadLettered =
            reader.GetBoolean(1);

        DateTimeOffset? nextAttemptAtUtc =
            reader.IsDBNull(2)
                ? null
                : reader.GetFieldValue<DateTimeOffset>(2);

        if (await reader.ReadAsync(
            cancellationToken))
        {
            throw new InvalidOperationException(
                "A failure update affected more than one Outbox message.");
        }

        return new CatalogOutboxFailureResult(
            true,
            deadLettered,
            attemptCount,
            nextAttemptAtUtc);
    }

    private static void ValidateMessageId(
        Guid messageId)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException(
                "The message identifier cannot be empty.",
                nameof(messageId));
        }
    }

    private static void ValidateLeaseOwner(
        string leaseOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            leaseOwner);

        if (leaseOwner.Length >
            LockOwnerMaximumLength)
        {
            throw new ArgumentException(
                "The lease owner exceeds the supported length.",
                nameof(leaseOwner));
        }
    }
    private static string CreateLeaseOwner(
        string workerId)
    {
        var token =
            Guid.CreateVersion7()
                .ToString(
                    "N",
                    CultureInfo.InvariantCulture);

        return string.Concat(
            workerId,
            ":",
            token);
    }
}
