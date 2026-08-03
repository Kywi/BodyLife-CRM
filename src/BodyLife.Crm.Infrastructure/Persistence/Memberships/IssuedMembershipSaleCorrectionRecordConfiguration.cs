using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class IssuedMembershipSaleCorrectionRecordConfiguration
    : IEntityTypeConfiguration<IssuedMembershipSaleCorrectionRecord>
{
    public void Configure(EntityTypeBuilder<IssuedMembershipSaleCorrectionRecord> builder)
    {
        builder.ToTable(
            "issued_membership_sale_corrections",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_mode",
                    "correction_mode in ('cancel', 'replace')");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_shape",
                    "(correction_mode = 'cancel' and replacement_membership_id is null and replacement_payment_id is null) or (correction_mode = 'replace' and replacement_membership_id is not null and replacement_payment_id is not null)");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_distinct_memberships",
                    "replacement_membership_id is null or replacement_membership_id <> original_membership_id");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_distinct_payments",
                    "replacement_payment_id is null or replacement_payment_id <> original_payment_id");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_reason",
                    "length(btrim(reason)) > 0");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_status",
                    "status = 'active'");
                table.HasCheckConstraint(
                    "ck_issued_membership_sale_corrections_token",
                    "length(btrim(dependency_token)) > 0");
            });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.ClientId).HasColumnName("client_id");
        builder.Property(row => row.OriginalMembershipId).HasColumnName("original_membership_id");
        builder.Property(row => row.OriginalPaymentId).HasColumnName("original_payment_id");
        builder.Property(row => row.ReplacementMembershipId).HasColumnName("replacement_membership_id");
        builder.Property(row => row.ReplacementPaymentId).HasColumnName("replacement_payment_id");
        builder.Property(row => row.CorrectionMode)
            .HasColumnName("correction_mode")
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(row => row.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();
        builder.Property(row => row.OccurredAt).HasColumnName("occurred_at");
        builder.Property(row => row.RecordedAt).HasColumnName("recorded_at");
        builder.Property(row => row.RecordedByAccountId).HasColumnName("recorded_by_account_id");
        builder.Property(row => row.SessionId).HasColumnName("session_id");
        builder.Property(row => row.EntryOrigin)
            .HasColumnName("entry_origin")
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(row => row.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(row => row.DependencyToken)
            .HasColumnName("dependency_token")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(row => row.OriginalMembershipId)
            .IsUnique()
            .HasDatabaseName("ux_issued_sale_corrections_original_membership");
        builder.HasIndex(row => row.OriginalPaymentId)
            .IsUnique()
            .HasDatabaseName("ux_issued_sale_corrections_original_payment");
        builder.HasIndex(row => row.ReplacementMembershipId)
            .IsUnique()
            .HasFilter("replacement_membership_id is not null")
            .HasDatabaseName("ux_issued_sale_corrections_replacement_membership");
        builder.HasIndex(row => row.ReplacementPaymentId)
            .IsUnique()
            .HasFilter("replacement_payment_id is not null")
            .HasDatabaseName("ux_issued_sale_corrections_replacement_payment");

        builder.HasOne<ClientRecord>()
            .WithMany()
            .HasForeignKey(row => row.ClientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.OriginalMembershipId,
                row.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName(
                "FK_issued_sale_corrections_original_membership_client")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.ReplacementMembershipId,
                row.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName(
                "FK_issued_sale_corrections_replacement_membership_client")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentRecord>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.OriginalPaymentId,
                row.ClientId,
            })
            .HasPrincipalKey(payment => new
            {
                payment.Id,
                payment.ClientId,
            })
            .HasConstraintName(
                "FK_issued_sale_corrections_original_payment_client")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PaymentRecord>()
            .WithMany()
            .HasForeignKey(row => new
            {
                row.ReplacementPaymentId,
                row.ClientId,
            })
            .HasPrincipalKey(payment => new
            {
                payment.Id,
                payment.ClientId,
            })
            .HasConstraintName(
                "FK_issued_sale_corrections_replacement_payment_client")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(row => row.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(row => row.SessionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class MembershipReplacementDependencyItemRecordConfiguration
    : IEntityTypeConfiguration<MembershipReplacementDependencyItemRecord>
{
    public void Configure(EntityTypeBuilder<MembershipReplacementDependencyItemRecord> builder)
    {
        builder.ToTable(
            "membership_replacement_dependency_items",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_replacement_dependency_items_type",
                    "dependency_type in ('visit', 'freeze', 'non_working_day_application', 'negative_coverage')");
                table.HasCheckConstraint(
                    "ck_membership_replacement_dependency_items_distinct_facts",
                    "replacement_fact_id is null or replacement_fact_id <> original_fact_id");
                table.HasCheckConstraint(
                    "ck_membership_replacement_dependency_items_validation",
                    "length(btrim(validation_summary)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_replacement_dependency_items_status",
                    "status = 'validated'");
            });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.SaleCorrectionId).HasColumnName("sale_correction_id");
        builder.Property(row => row.DependencyType)
            .HasColumnName("dependency_type")
            .HasMaxLength(48)
            .IsRequired();
        builder.Property(row => row.OriginalFactId)
            .HasColumnName("original_fact_id");
        builder.Property(row => row.ReplacementFactId)
            .HasColumnName("replacement_fact_id");
        builder.Property(row => row.ValidationSummary)
            .HasColumnName("validation_summary")
            .HasMaxLength(1000)
            .IsRequired();
        builder.Property(row => row.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();
        builder.HasIndex(row => new
        {
            row.SaleCorrectionId,
            row.DependencyType,
            row.OriginalFactId,
        })
            .IsUnique()
            .HasDatabaseName("ux_membership_replacement_dependencies_original");
        builder.HasOne<IssuedMembershipSaleCorrectionRecord>()
            .WithMany()
            .HasForeignKey(row => row.SaleCorrectionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
