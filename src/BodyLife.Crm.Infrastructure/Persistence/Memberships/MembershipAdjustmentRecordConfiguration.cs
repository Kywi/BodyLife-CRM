using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipAdjustmentRecordConfiguration
    : IEntityTypeConfiguration<MembershipAdjustmentRecord>
{
    public void Configure(EntityTypeBuilder<MembershipAdjustmentRecord> builder)
    {
        builder.ToTable(
            "membership_adjustments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_adjustments_adjustment_type_not_empty",
                    "length(btrim(adjustment_type)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_adjustments_delta_non_zero",
                    "coalesce(days_delta, 0) <> 0 or coalesce(visits_delta, 0) <> 0 or coalesce(money_delta, 0) <> 0");
                table.HasCheckConstraint(
                    "ck_membership_adjustments_reason_not_empty",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_adjustments_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_membership_adjustments_status",
                    "status in ('active', 'canceled', 'corrected')");
            });

        builder.HasKey(adjustment => adjustment.Id);

        builder.Property(adjustment => adjustment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(adjustment => adjustment.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(adjustment => adjustment.AdjustmentType)
            .HasColumnName("adjustment_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(adjustment => adjustment.DaysDelta)
            .HasColumnName("days_delta");

        builder.Property(adjustment => adjustment.VisitsDelta)
            .HasColumnName("visits_delta");

        builder.Property(adjustment => adjustment.MoneyDelta)
            .HasColumnName("money_delta")
            .HasColumnType("numeric");

        builder.Property(adjustment => adjustment.EffectiveDate)
            .HasColumnName("effective_date")
            .HasColumnType("date");

        builder.Property(adjustment => adjustment.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(adjustment => adjustment.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(adjustment => adjustment.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(adjustment => adjustment.RecordedSessionId)
            .HasColumnName("recorded_session_id");

        builder.Property(adjustment => adjustment.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(adjustment => adjustment.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(adjustment => adjustment.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(adjustment => adjustment.Membership)
            .WithMany()
            .HasForeignKey(adjustment => adjustment.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(adjustment => adjustment.RecordedSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(adjustment => new
        {
            adjustment.MembershipId,
            adjustment.EffectiveDate,
            adjustment.AdjustmentType,
        })
            .HasFilter("status = 'active'")
            .HasDatabaseName("ix_membership_adjustments_active_membership_effective_date");

        builder.HasIndex(adjustment => new
        {
            adjustment.MembershipId,
            adjustment.EffectiveDate,
            adjustment.RecordedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_membership_adjustments_membership_timeline");

        builder.HasIndex(adjustment => adjustment.RecordedByAccountId)
            .HasDatabaseName("ix_membership_adjustments_recorded_by_account_id");

        builder.HasIndex(adjustment => adjustment.RecordedSessionId)
            .HasDatabaseName("ix_membership_adjustments_recorded_session_id");
    }
}
