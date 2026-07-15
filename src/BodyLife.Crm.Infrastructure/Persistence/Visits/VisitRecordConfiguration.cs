using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class VisitRecordConfiguration : IEntityTypeConfiguration<VisitRecord>
{
    public void Configure(EntityTypeBuilder<VisitRecord> builder)
    {
        builder.ToTable(
            "visits",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_visits_visit_kind",
                    "visit_kind in ('membership', 'one_off', 'trial')");
                table.HasCheckConstraint(
                    "ck_visits_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_visits_comment_not_empty",
                    "comment is null or length(btrim(comment)) > 0");
                table.HasCheckConstraint(
                    "ck_visits_status",
                    "status in ('active', 'canceled')");
            });

        builder.HasKey(visit => visit.Id);

        builder.HasAlternateKey(visit => new
        {
            visit.Id,
            visit.ClientId,
            visit.VisitKind,
        })
            .HasName("AK_visits_id_client_id_visit_kind");

        builder.Property(visit => visit.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(visit => visit.ClientId)
            .HasColumnName("client_id");

        builder.Property(visit => visit.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(visit => visit.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(visit => visit.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(visit => visit.SessionId)
            .HasColumnName("session_id");

        builder.Property(visit => visit.VisitKind)
            .HasColumnName("visit_kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(visit => visit.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(visit => visit.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(visit => visit.Comment)
            .HasColumnName("comment")
            .HasMaxLength(1000);

        builder.Property(visit => visit.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(visit => visit.Client)
            .WithMany()
            .HasForeignKey(visit => visit.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(visit => visit.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(visit => visit.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(visit => new
        {
            visit.OccurredAt,
            visit.ClientId,
        })
            .HasFilter("status = 'active'")
            .HasDatabaseName("ix_visits_active_daily_report");

        builder.HasIndex(visit => new
        {
            visit.OccurredAt,
            visit.Status,
            visit.ClientId,
        })
            .HasDatabaseName("ix_visits_daily_source");

        builder.HasIndex(visit => new
        {
            visit.ClientId,
            visit.OccurredAt,
            visit.RecordedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_visits_client_timeline");

        builder.HasIndex(visit => visit.RecordedByAccountId)
            .HasDatabaseName("ix_visits_recorded_by_account_id");

        builder.HasIndex(visit => visit.SessionId)
            .HasDatabaseName("ix_visits_session_id");
    }
}
