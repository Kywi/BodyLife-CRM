using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class IssuedMembershipRecordConfiguration
    : IEntityTypeConfiguration<IssuedMembershipRecord>
{
    public void Configure(EntityTypeBuilder<IssuedMembershipRecord> builder)
    {
        builder.ToTable(
            "issued_memberships",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_issued_memberships_type_name_snapshot_not_empty",
                    "length(btrim(type_name_snapshot)) > 0");
                table.HasCheckConstraint(
                    "ck_issued_memberships_duration_snapshot_positive",
                    "duration_days_snapshot > 0");
                table.HasCheckConstraint(
                    "ck_issued_memberships_visits_snapshot_non_negative",
                    "visits_limit_snapshot >= 0");
                table.HasCheckConstraint(
                    "ck_issued_memberships_price_snapshot_non_negative",
                    "price_amount_snapshot >= 0");
                table.HasCheckConstraint(
                    "ck_issued_memberships_currency_snapshot_canonical",
                    "length(btrim(price_currency_snapshot)) > 0 and price_currency_snapshot = upper(btrim(price_currency_snapshot))");
                table.HasCheckConstraint(
                    "ck_issued_memberships_base_end_date",
                    "base_end_date = start_date + (duration_days_snapshot - 1)");
                table.HasCheckConstraint(
                    "ck_issued_memberships_status",
                    "status in ('active', 'canceled', 'corrected')");
                table.HasCheckConstraint(
                    "ck_issued_memberships_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_issued_memberships_comment_not_empty",
                    "comment is null or length(btrim(comment)) > 0");
            });

        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(membership => membership.ClientId)
            .HasColumnName("client_id");

        builder.Property(membership => membership.MembershipTypeId)
            .HasColumnName("membership_type_id");

        builder.Property(membership => membership.TypeNameSnapshot)
            .HasColumnName("type_name_snapshot")
            .IsRequired();

        builder.Property(membership => membership.DurationDaysSnapshot)
            .HasColumnName("duration_days_snapshot");

        builder.Property(membership => membership.VisitsLimitSnapshot)
            .HasColumnName("visits_limit_snapshot");

        builder.Property(membership => membership.PriceAmountSnapshot)
            .HasColumnName("price_amount_snapshot")
            .HasColumnType("numeric");

        builder.Property(membership => membership.PriceCurrencySnapshot)
            .HasColumnName("price_currency_snapshot")
            .IsRequired();

        builder.Property(membership => membership.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date");

        builder.Property(membership => membership.BaseEndDate)
            .HasColumnName("base_end_date")
            .HasColumnType("date");

        builder.Property(membership => membership.IssuedAt)
            .HasColumnName("issued_at");

        builder.Property(membership => membership.IssuedByAccountId)
            .HasColumnName("issued_by_account_id");

        builder.Property(membership => membership.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(membership => membership.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(membership => membership.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(membership => membership.Comment)
            .HasColumnName("comment");

        builder.HasOne<ClientRecord>()
            .WithMany()
            .HasForeignKey(membership => membership.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MembershipTypeRecord>()
            .WithMany()
            .HasForeignKey(membership => membership.MembershipTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(membership => membership.IssuedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(membership => new
        {
            membership.ClientId,
            membership.StartDate,
            membership.IssuedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_issued_memberships_client_timeline");

        builder.HasIndex(membership => membership.MembershipTypeId)
            .HasDatabaseName("ix_issued_memberships_membership_type_id");

        builder.HasIndex(membership => membership.IssuedByAccountId)
            .HasDatabaseName("ix_issued_memberships_issued_by_account_id");
    }
}
