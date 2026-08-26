using Catalog.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageRecordConfiguration :
    IEntityTypeConfiguration<OutboxMessageRecord>
{
    private const int MessageTypeMaximumLength = 200;
    private const int LockOwnerMaximumLength = 128;
    private const int LastErrorCodeMaximumLength = 128;
    private const int TraceParentMaximumLength = 55;
    private const int TraceStateMaximumLength = 512;

    public void Configure(
        EntityTypeBuilder<OutboxMessageRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
        "outbox_messages",
        CatalogDbContext.Schema,
        tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_outbox_messages_attempt_count",
                "attempt_count >= 0");

            tableBuilder.HasCheckConstraint(
                "ck_outbox_messages_terminal_state",
                "processed_at_utc IS NULL OR dead_lettered_at_utc IS NULL");

            tableBuilder.HasCheckConstraint(
                "ck_outbox_messages_lease_pair",
                "(lock_owner IS NULL AND locked_until_utc IS NULL) OR " +
                "(lock_owner IS NOT NULL AND locked_until_utc IS NOT NULL)");

            tableBuilder.HasCheckConstraint(
                "ck_outbox_messages_terminal_lease",
                "((processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL) OR " +
                "(lock_owner IS NULL AND locked_until_utc IS NULL))");

            tableBuilder.HasCheckConstraint(
                "ck_outbox_messages_next_attempt",
                "next_attempt_at_utc >= enqueued_at_utc");
        });

        builder.HasKey(
                message => message.Id)
            .HasName(
                "pk_outbox_messages");

        builder.Property(
                message => message.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever();

        builder.Property(
                message => message.MessageType)
            .HasColumnName("type")
            .HasMaxLength(
                MessageTypeMaximumLength)
            .IsRequired();

        builder.Property(
                message => message.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(
                message => message.OccurredAtUtc)
            .HasColumnName(
                "occurred_at_utc")
            .HasColumnType(
                "timestamp with time zone")
            .IsRequired();

        builder.Property(
                message => message.EnqueuedAtUtc)
            .HasColumnName(
                "enqueued_at_utc")
            .HasColumnType(
                "timestamp with time zone")
            .HasDefaultValueSql(
                "CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(
                message => message.AttemptCount)
            .HasColumnName(
                "attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(
                message => message.NextAttemptAtUtc)
            .HasColumnName(
                "next_attempt_at_utc")
            .HasColumnType(
                "timestamp with time zone")
            .HasDefaultValueSql(
                "CURRENT_TIMESTAMP")
            .ValueGeneratedOnAdd()
            .IsRequired();

        builder.Property(
                message => message.LockedUntilUtc)
            .HasColumnName(
                "locked_until_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message => message.LockOwner)
            .HasColumnName(
                "lock_owner")
            .HasMaxLength(
                LockOwnerMaximumLength);

        builder.Property(
                message => message.ProcessedAtUtc)
            .HasColumnName(
                "processed_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message => message.DeadLetteredAtUtc)
            .HasColumnName(
                "dead_lettered_at_utc")
            .HasColumnType(
                "timestamp with time zone");

        builder.Property(
                message => message.LastErrorCode)
            .HasColumnName(
                "last_error_code")
            .HasMaxLength(
                LastErrorCodeMaximumLength);

        builder.Property(
                message => message.TraceParent)
            .HasColumnName(
                "trace_parent")
            .HasMaxLength(
                TraceParentMaximumLength);

        builder.Property(
                message => message.TraceState)
            .HasColumnName(
                "trace_state")
            .HasMaxLength(
                TraceStateMaximumLength);

        builder.HasIndex(
            message => new
            {
                message.NextAttemptAtUtc,
                message.OccurredAtUtc,
                message.Id
            })
        .HasDatabaseName(
            "ix_outbox_messages_pending")
        .HasFilter(
            "processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL");
    }
}
