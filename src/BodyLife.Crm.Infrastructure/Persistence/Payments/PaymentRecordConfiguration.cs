using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class PaymentRecordConfiguration : IEntityTypeConfiguration<PaymentRecord>
{
    public void Configure(EntityTypeBuilder<PaymentRecord> builder)
    {
        builder.ToTable(
            "payments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payments_amount_positive",
                    "amount > 0");
                table.HasCheckConstraint(
                    "ck_payments_currency_canonical",
                    "length(btrim(currency)) > 0 and currency = upper(btrim(currency))");
                table.HasCheckConstraint(
                    "ck_payments_method",
                    "method = 'cash'");
                table.HasCheckConstraint(
                    "ck_payments_payment_context",
                    "payment_context in ('membership_sale', 'one_off', 'trial', 'negative_closure', 'other')");
                table.HasCheckConstraint(
                    "ck_payments_membership_sale_membership",
                    "payment_context <> 'membership_sale' or membership_id is not null");
                table.HasCheckConstraint(
                    "ck_payments_negative_closure_context",
                    "(payment_context = 'negative_closure' and negative_closure_id is not null and membership_id is null) or (payment_context <> 'negative_closure' and negative_closure_id is null)");
                table.HasCheckConstraint(
                    "ck_payments_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_payments_comment_not_empty",
                    "comment is null or length(btrim(comment)) > 0");
                table.HasCheckConstraint(
                    "ck_payments_status",
                    "status in ('active', 'canceled', 'replaced')");
            });

        builder.HasKey(payment => payment.Id);

        builder.HasAlternateKey(payment => new
        {
            payment.Id,
            payment.ClientId,
        })
            .HasName("AK_payments_id_client_id");

        builder.Property(payment => payment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(payment => payment.ClientId)
            .HasColumnName("client_id");

        builder.Property(payment => payment.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(payment => payment.NegativeClosureId)
            .HasColumnName("negative_closure_id");

        builder.Property(payment => payment.Amount)
            .HasColumnName("amount")
            .HasColumnType("numeric");

        builder.Property(payment => payment.Currency)
            .HasColumnName("currency")
            .IsRequired();

        builder.Property(payment => payment.Method)
            .HasColumnName("method")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(payment => payment.PaymentContext)
            .HasColumnName("payment_context")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(payment => payment.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(payment => payment.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(payment => payment.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(payment => payment.SessionId)
            .HasColumnName("session_id");

        builder.Property(payment => payment.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(payment => payment.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.Property(payment => payment.Comment)
            .HasColumnName("comment")
            .HasMaxLength(1000);

        builder.Property(payment => payment.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(payment => payment.Client)
            .WithMany()
            .HasForeignKey(payment => payment.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(payment => payment.Membership)
            .WithMany()
            .HasForeignKey(payment => new
            {
                payment.MembershipId,
                payment.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName("FK_payments_issued_memberships_membership_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MembershipNegativeClosureRecord>()
            .WithMany()
            .HasForeignKey(payment => new
            {
                payment.NegativeClosureId,
                payment.ClientId,
            })
            .HasPrincipalKey(closure => new
            {
                closure.Id,
                closure.ClientId,
            })
            .HasConstraintName("FK_payments_negative_closures_closure_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(payment => payment.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(payment => payment.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(payment => new
        {
            payment.OccurredAt,
            payment.Method,
            payment.ClientId,
        })
            .IncludeProperties(payment => payment.Amount)
            .HasFilter("status = 'active'")
            .HasDatabaseName("ix_payments_active_daily_report");

        builder.HasIndex(payment => new
        {
            payment.OccurredAt,
            payment.Status,
            payment.Method,
            payment.ClientId,
        })
            .IncludeProperties(payment => payment.Amount)
            .HasDatabaseName("ix_payments_daily_source");

        builder.HasIndex(payment => new
        {
            payment.ClientId,
            payment.OccurredAt,
            payment.RecordedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_payments_client_timeline");

        builder.HasIndex(payment => new
        {
            payment.MembershipId,
            payment.ClientId,
            payment.OccurredAt,
        })
            .IsDescending(false, false, true)
            .HasFilter("membership_id is not null")
            .HasDatabaseName("ix_payments_membership_timeline");

        builder.HasIndex(payment => payment.MembershipId)
            .IsUnique()
            .HasFilter("payment_context = 'membership_sale'")
            .HasDatabaseName("ux_payments_membership_sale_membership");

        builder.HasIndex(payment => payment.NegativeClosureId)
            .IsUnique()
            .HasFilter("payment_context = 'negative_closure' and status = 'active'")
            .HasDatabaseName("ux_payments_active_negative_closure");

        builder.HasIndex(payment => payment.RecordedByAccountId)
            .HasDatabaseName("ix_payments_recorded_by_account_id");

        builder.HasIndex(payment => payment.SessionId)
            .HasDatabaseName("ix_payments_session_id");
    }
}
