using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class PaymentCorrectionRecordConfiguration
    : IEntityTypeConfiguration<PaymentCorrectionRecord>
{
    public void Configure(EntityTypeBuilder<PaymentCorrectionRecord> builder)
    {
        builder.ToTable(
            "payment_corrections",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_payment_corrections_distinct_payments",
                    "original_payment_id <> replacement_payment_id");
                table.HasCheckConstraint(
                    "ck_payment_corrections_changed_fields",
                    "jsonb_typeof(changed_fields) = 'array' and jsonb_array_length(changed_fields) > 0");
                table.HasCheckConstraint(
                    "ck_payment_corrections_reason_not_empty",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_payment_corrections_entry_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
            });

        builder.HasKey(correction => correction.Id);

        builder.Property(correction => correction.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(correction => correction.ClientId)
            .HasColumnName("client_id");

        builder.Property(correction => correction.OriginalPaymentId)
            .HasColumnName("original_payment_id");

        builder.Property(correction => correction.ReplacementPaymentId)
            .HasColumnName("replacement_payment_id");

        builder.Property(correction => correction.ChangedFieldsJson)
            .HasColumnName("changed_fields")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(correction => correction.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(correction => correction.OccurredAt)
            .HasColumnName("occurred_at");

        builder.Property(correction => correction.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(correction => correction.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(correction => correction.SessionId)
            .HasColumnName("session_id");

        builder.Property(correction => correction.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(correction => correction.EntryBatchId)
            .HasColumnName("entry_batch_id");

        builder.HasOne(correction => correction.OriginalPayment)
            .WithMany()
            .HasForeignKey(correction => new
            {
                correction.OriginalPaymentId,
                correction.ClientId,
            })
            .HasPrincipalKey(payment => new
            {
                payment.Id,
                payment.ClientId,
            })
            .HasConstraintName("FK_payment_corrections_payments_original_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(correction => correction.ReplacementPayment)
            .WithMany()
            .HasForeignKey(correction => new
            {
                correction.ReplacementPaymentId,
                correction.ClientId,
            })
            .HasPrincipalKey(payment => new
            {
                payment.Id,
                payment.ClientId,
            })
            .HasConstraintName("FK_payment_corrections_payments_replacement_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(correction => correction.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(correction => correction.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(correction => new
        {
            correction.OriginalPaymentId,
            correction.ClientId,
        })
            .IsUnique()
            .HasDatabaseName("ux_payment_corrections_original_payment_id");

        builder.HasIndex(correction => new
        {
            correction.ReplacementPaymentId,
            correction.ClientId,
        })
            .IsUnique()
            .HasDatabaseName("ux_payment_corrections_replacement_payment_id");

        builder.HasIndex(correction => new
        {
            correction.ClientId,
            correction.OccurredAt,
            correction.RecordedAt,
        })
            .IsDescending(false, true, true)
            .HasDatabaseName("ix_payment_corrections_client_timeline");

        builder.HasIndex(correction => correction.RecordedByAccountId)
            .HasDatabaseName("ix_payment_corrections_recorded_by_account_id");

        builder.HasIndex(correction => correction.SessionId)
            .HasDatabaseName("ix_payment_corrections_session_id");
    }
}
