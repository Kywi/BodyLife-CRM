using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodApplicationRecordConfiguration
    : IEntityTypeConfiguration<NonWorkingPeriodApplicationRecord>
{
    public void Configure(EntityTypeBuilder<NonWorkingPeriodApplicationRecord> builder)
    {
        builder.ToTable(
            "non_working_period_applications",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_non_working_period_applications_inclusive_range",
                    "applied_start_date <= applied_end_date");
                table.HasCheckConstraint(
                    "ck_non_working_period_applications_preview_order",
                    "previewed_at <= confirmed_at");
                table.HasCheckConstraint(
                    "ck_non_working_period_applications_status",
                    "status in ('active', 'canceled', 'corrected')");
            });

        builder.HasKey(application => application.Id);

        builder.Property(application => application.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(application => application.NonWorkingPeriodId)
            .HasColumnName("non_working_period_id");

        builder.Property(application => application.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(application => application.ClientId)
            .HasColumnName("client_id");

        builder.Property(application => application.AppliedStartDate)
            .HasColumnName("applied_start_date")
            .HasColumnType("date");

        builder.Property(application => application.AppliedEndDate)
            .HasColumnName("applied_end_date")
            .HasColumnType("date");

        builder.Property(application => application.PreviewedAt)
            .HasColumnName("previewed_at");

        builder.Property(application => application.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(application => application.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(application => application.NonWorkingPeriod)
            .WithMany()
            .HasForeignKey(application => new
            {
                application.NonWorkingPeriodId,
                application.AppliedStartDate,
                application.AppliedEndDate,
            })
            .HasPrincipalKey(period => new
            {
                period.Id,
                period.StartDate,
                period.EndDate,
            })
            .HasConstraintName("FK_non_working_period_applications_period_range")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(application => application.Membership)
            .WithMany()
            .HasForeignKey(application => new
            {
                application.MembershipId,
                application.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName("FK_non_working_period_applications_membership_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(application => new
        {
            application.NonWorkingPeriodId,
            application.MembershipId,
        })
            .IsUnique()
            .HasFilter("status = 'active'")
            .HasDatabaseName("ux_non_working_applications_active_period_membership");

        builder.HasIndex(application => new
        {
            application.MembershipId,
            application.Status,
            application.AppliedStartDate,
            application.AppliedEndDate,
        })
            .HasDatabaseName("ix_non_working_applications_membership_status_range");

        builder.HasIndex(application => new
        {
            application.ClientId,
            application.ConfirmedAt,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_non_working_applications_client_timeline");
    }
}
