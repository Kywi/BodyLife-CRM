using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Idempotency;

internal sealed class CommandIdempotencyRecordConfiguration : IEntityTypeConfiguration<CommandIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<CommandIdempotencyRecord> builder)
    {
        builder.ToTable(
            "command_idempotency_keys",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_command_name_not_empty",
                    "length(btrim(command_name)) > 0");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_key_not_empty",
                    "length(btrim(idempotency_key)) > 0");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_correlation_not_empty",
                    "length(btrim(request_correlation_id)) > 0");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_actor_role_not_empty",
                    "length(btrim(actor_role)) > 0");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_account_kind_not_empty",
                    "length(btrim(account_kind)) > 0");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_status",
                    "status in ('started', 'succeeded', 'failed')");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_expires_after_created",
                    "expires_at > created_at");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_completed_after_created",
                    "completed_at is null or completed_at >= created_at");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_started_not_completed",
                    "(status <> 'started') or completed_at is null");
                table.HasCheckConstraint(
                    "ck_command_idempotency_keys_completed_status_has_time",
                    "(status = 'started') or completed_at is not null");
            });

        builder.HasKey(record => record.Id);

        builder.Property(record => record.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(record => record.CommandName)
            .HasColumnName("command_name")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(record => record.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record => record.RequestCorrelationId)
            .HasColumnName("request_correlation_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(record => record.AccountId)
            .HasColumnName("account_id");

        builder.Property(record => record.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record => record.AccountKind)
            .HasColumnName("account_kind")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(record => record.SessionId)
            .HasColumnName("session_id");

        builder.Property(record => record.DeviceLabel)
            .HasColumnName("device_label")
            .HasMaxLength(120);

        builder.Property(record => record.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(record => record.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(record => record.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(record => record.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(record => record.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(record => record.PrimaryEntityId)
            .HasColumnName("primary_entity_id");

        builder.Property(record => record.RereadTargetId)
            .HasColumnName("reread_target_id");

        builder.Property(record => record.AuditEntryId)
            .HasColumnName("audit_entry_id");

        builder.Property(record => record.ResultFingerprint)
            .HasColumnName("result_fingerprint")
            .HasMaxLength(128);

        builder.HasIndex(record => new { record.CommandName, record.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("ux_command_idempotency_keys_command_key");

        builder.HasIndex(record => record.ExpiresAt)
            .HasDatabaseName("ix_command_idempotency_keys_expires_at");
    }
}
