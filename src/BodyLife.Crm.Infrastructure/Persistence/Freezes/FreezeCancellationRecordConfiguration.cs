using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal sealed class FreezeCancellationRecordConfiguration
    : IEntityTypeConfiguration<FreezeCancellationRecord>
{
    public void Configure(EntityTypeBuilder<FreezeCancellationRecord> builder)
    {
        builder.ToTable(
            "freeze_cancellations",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_freeze_cancellations_reason_not_empty",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_freeze_cancellations_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
            });

        builder.HasKey(cancellation => cancellation.Id);

        builder.Property(cancellation => cancellation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cancellation => cancellation.FreezeId)
            .HasColumnName("freeze_id");

        builder.Property(cancellation => cancellation.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(cancellation => cancellation.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(cancellation => cancellation.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(cancellation => cancellation.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(cancellation => cancellation.SessionId)
            .HasColumnName("session_id");

        builder.Property(cancellation => cancellation.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(cancellation => cancellation.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.HasOne(cancellation => cancellation.Freeze)
            .WithMany()
            .HasForeignKey(cancellation => cancellation.FreezeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(cancellation => cancellation.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(cancellation => cancellation.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cancellation => cancellation.FreezeId)
            .IsUnique()
            .HasDatabaseName("ux_freeze_cancellations_freeze_id");

        builder.HasIndex(cancellation => new
        {
            cancellation.OccurredAt,
            cancellation.RecordedAt,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_freeze_cancellations_timeline");

        builder.HasIndex(cancellation => cancellation.RecordedByAccountId)
            .HasDatabaseName("ix_freeze_cancellations_recorded_by_account_id");

        builder.HasIndex(cancellation => cancellation.SessionId)
            .HasDatabaseName("ix_freeze_cancellations_session_id");
    }
}
