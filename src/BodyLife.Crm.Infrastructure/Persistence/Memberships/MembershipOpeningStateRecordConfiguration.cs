using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipOpeningStateRecordConfiguration
    : IEntityTypeConfiguration<MembershipOpeningStateRecord>
{
    public void Configure(EntityTypeBuilder<MembershipOpeningStateRecord> builder)
    {
        builder.ToTable(
            "membership_opening_states",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_opening_states_negative_balance_consistent",
                    "declared_negative_balance = greatest(0::bigint, -(declared_remaining_visits::bigint))");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_known_extension_days_non_negative",
                    "known_extension_days is null or known_extension_days >= 0");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_known_end_not_before_opening",
                    "known_effective_end_date is null or known_effective_end_date >= opening_as_of_date");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_source_reference_not_empty",
                    "length(btrim(source_reference)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_reason_not_empty",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_entry_origin",
                    "entry_origin in ('manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_membership_opening_states_status",
                    "status in ('active', 'canceled', 'corrected')");
            });

        builder.HasKey(openingState => openingState.Id);

        builder.Property(openingState => openingState.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(openingState => openingState.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(openingState => openingState.OpeningAsOfDate)
            .HasColumnName("opening_as_of_date")
            .HasColumnType("date");

        builder.Property(openingState => openingState.DeclaredRemainingVisits)
            .HasColumnName("declared_remaining_visits");

        builder.Property(openingState => openingState.DeclaredNegativeBalance)
            .HasColumnName("declared_negative_balance");

        builder.Property(openingState => openingState.KnownEffectiveEndDate)
            .HasColumnName("known_effective_end_date")
            .HasColumnType("date");

        builder.Property(openingState => openingState.KnownExtensionDays)
            .HasColumnName("known_extension_days");

        builder.Property(openingState => openingState.SourceReference)
            .HasColumnName("source_reference")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(openingState => openingState.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(openingState => openingState.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(openingState => openingState.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(openingState => openingState.RecordedSessionId)
            .HasColumnName("recorded_session_id");

        builder.Property(openingState => openingState.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(openingState => openingState.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(openingState => openingState.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(openingState => openingState.Membership)
            .WithMany()
            .HasForeignKey(openingState => openingState.MembershipId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(openingState => openingState.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(openingState => openingState.RecordedSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(openingState => openingState.MembershipId)
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_membership_opening_states_active_membership");

        builder.HasIndex(openingState => new
        {
            openingState.MembershipId,
            openingState.OpeningAsOfDate,
            openingState.RecordedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_membership_opening_states_membership_timeline");

        builder.HasIndex(openingState => openingState.RecordedByAccountId)
            .HasDatabaseName("ix_membership_opening_states_recorded_by_account_id");

        builder.HasIndex(openingState => openingState.RecordedSessionId)
            .HasDatabaseName("ix_membership_opening_states_recorded_session_id");
    }
}
