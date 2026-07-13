using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipStateCacheRecordConfiguration
    : IEntityTypeConfiguration<MembershipStateCacheRecord>
{
    public void Configure(EntityTypeBuilder<MembershipStateCacheRecord> builder)
    {
        builder.ToTable(
            "membership_state_cache",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_state_cache_counted_visits_non_negative",
                    "counted_visits >= 0");
                table.HasCheckConstraint(
                    "ck_membership_state_cache_negative_balance_consistent",
                    "negative_balance = greatest(0::bigint, -(remaining_visits::bigint))");
                table.HasCheckConstraint(
                    "ck_membership_state_cache_extension_days_non_negative",
                    "extension_days >= 0");
                table.HasCheckConstraint(
                    "ck_membership_state_cache_recalculation_version_positive",
                    "recalculation_version > 0");
            });

        builder.HasKey(state => state.MembershipId);

        builder.Property(state => state.MembershipId)
            .HasColumnName("membership_id")
            .ValueGeneratedNever();

        builder.Property(state => state.CountedVisits)
            .HasColumnName("counted_visits");

        builder.Property(state => state.RemainingVisits)
            .HasColumnName("remaining_visits");

        builder.Property(state => state.NegativeBalance)
            .HasColumnName("negative_balance");

        builder.Property(state => state.FirstNegativeVisitId)
            .HasColumnName("first_negative_visit_id");

        builder.Property(state => state.FirstNegativeVisitDate)
            .HasColumnName("first_negative_visit_date")
            .HasColumnType("date");

        builder.Property(state => state.ExtensionDays)
            .HasColumnName("extension_days");

        builder.Property(state => state.EffectiveEndDate)
            .HasColumnName("effective_end_date")
            .HasColumnType("date");

        builder.Property(state => state.LastCountedVisitAt)
            .HasColumnName("last_counted_visit_at");

        builder.Property(state => state.RecalculatedAt)
            .HasColumnName("recalculated_at");

        builder.Property(state => state.RecalculationVersion)
            .HasColumnName("recalculation_version");

        builder.HasOne(state => state.Membership)
            .WithOne()
            .HasForeignKey<MembershipStateCacheRecord>(state => state.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(state => state.EffectiveEndDate)
            .HasDatabaseName("ix_membership_state_cache_effective_end_date");

        builder.HasIndex(state => state.RemainingVisits)
            .HasDatabaseName("ix_membership_state_cache_remaining_visits");

        builder.HasIndex(state => state.NegativeBalance)
            .HasFilter("negative_balance > 0")
            .HasDatabaseName("ix_membership_state_cache_negative_balance_open");

        builder.HasIndex(state => state.LastCountedVisitAt)
            .HasDatabaseName("ix_membership_state_cache_last_counted_visit_at");
    }
}
