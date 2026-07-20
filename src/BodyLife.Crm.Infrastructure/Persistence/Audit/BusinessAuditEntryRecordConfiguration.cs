using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal sealed class BusinessAuditEntryRecordConfiguration
    : IEntityTypeConfiguration<BusinessAuditEntryRecord>
{
    public void Configure(EntityTypeBuilder<BusinessAuditEntryRecord> builder)
    {
        builder.ToTable(
            "business_audit_entries",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_business_audit_entries_action_type_not_empty",
                    "length(btrim(action_type)) > 0");
                table.HasCheckConstraint(
                    "ck_business_audit_entries_entity_type_not_empty",
                    "length(btrim(entity_type)) > 0");
                table.HasCheckConstraint(
                    "ck_business_audit_entries_actor_account_type",
                    "actor_account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                table.HasCheckConstraint(
                    "ck_business_audit_entries_actor_role",
                    "actor_role in ('owner', 'admin')");
                table.HasCheckConstraint(
                    "ck_business_audit_entries_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_business_audit_entries_correlation_not_empty",
                    "length(btrim(request_correlation_id)) > 0");
            });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.ActionType)
            .HasColumnName("action_type")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entry => entry.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(entry => entry.EntityId)
            .HasColumnName("entity_id");

        builder.Property(entry => entry.RelatedEntityRefsJson)
            .HasColumnName("related_entity_refs")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(entry => entry.ActorAccountId)
            .HasColumnName("actor_account_id");

        builder.Property(entry => entry.ActorAccountType)
            .HasColumnName("actor_account_type")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(entry => entry.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.SessionId)
            .HasColumnName("session_id");

        builder.Property(entry => entry.DeviceLabel)
            .HasColumnName("device_label")
            .HasMaxLength(120);

        builder.Property(entry => entry.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(entry => entry.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(entry => entry.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000);

        builder.Property(entry => entry.Comment)
            .HasColumnName("comment")
            .HasMaxLength(2000);

        builder.Property(entry => entry.BeforeSummaryJson)
            .HasColumnName("before_summary")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(entry => entry.AfterSummaryJson)
            .HasColumnName("after_summary")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(entry => entry.RequestCorrelationId)
            .HasColumnName("request_correlation_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(entry => entry.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(entry => entry.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(200);

        builder.Property(entry => entry.ChangedAfterClose)
            .HasColumnName("changed_after_close");

        builder.HasIndex(entry => new { entry.EntityType, entry.EntityId, entry.RecordedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("ix_business_audit_entries_entity_timeline");

        builder.HasIndex(entry => new { entry.ActorAccountId, entry.RecordedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_business_audit_entries_actor_timeline");

        builder.HasIndex(entry => new { entry.RecordedAt, entry.Id })
            .IsDescending(true, true)
            .HasDatabaseName("ix_business_audit_entries_recorded_timeline");

        builder.HasIndex(entry => entry.RelatedEntityRefsJson)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops")
            .HasDatabaseName("ix_business_audit_entries_related_entity_refs");
    }
}
