using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal sealed class FreezeRecordConfiguration : IEntityTypeConfiguration<FreezeRecord>
{
    public void Configure(EntityTypeBuilder<FreezeRecord> builder)
    {
        builder.ToTable(
            "freezes",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_freezes_inclusive_range",
                    "start_date <= end_date");
                table.HasCheckConstraint(
                    "ck_freezes_reason_not_empty",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_freezes_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_freezes_status",
                    "status in ('active', 'canceled')");
            });

        builder.HasKey(freeze => freeze.Id);

        builder.Property(freeze => freeze.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(freeze => freeze.ClientId)
            .HasColumnName("client_id");

        builder.Property(freeze => freeze.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(freeze => freeze.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date");

        builder.Property(freeze => freeze.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date");

        builder.Property(freeze => freeze.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(freeze => freeze.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(freeze => freeze.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(freeze => freeze.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(freeze => freeze.SessionId)
            .HasColumnName("session_id");

        builder.Property(freeze => freeze.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(freeze => freeze.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(freeze => freeze.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(freeze => freeze.Membership)
            .WithMany()
            .HasForeignKey(freeze => new
            {
                freeze.MembershipId,
                freeze.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName("FK_freezes_issued_memberships_membership_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(freeze => freeze.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(freeze => freeze.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(freeze => new
        {
            freeze.MembershipId,
            freeze.Status,
            freeze.StartDate,
            freeze.EndDate,
        })
            .HasDatabaseName("ix_freezes_membership_status_range");

        builder.HasIndex(freeze => new
        {
            freeze.ClientId,
            freeze.RecordedAt,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_freezes_client_timeline");

        builder.HasIndex(freeze => freeze.RecordedByAccountId)
            .HasDatabaseName("ix_freezes_recorded_by_account_id");

        builder.HasIndex(freeze => freeze.SessionId)
            .HasDatabaseName("ix_freezes_session_id");
    }
}
