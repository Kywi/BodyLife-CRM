using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodRecordConfiguration
    : IEntityTypeConfiguration<NonWorkingPeriodRecord>
{
    public void Configure(EntityTypeBuilder<NonWorkingPeriodRecord> builder)
    {
        builder.ToTable(
            "non_working_periods",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_non_working_periods_inclusive_range",
                    "start_date <= end_date");
                table.HasCheckConstraint(
                    "ck_non_working_periods_reason_code_not_empty",
                    "length(btrim(reason_code)) > 0");
                table.HasCheckConstraint(
                    "ck_non_working_periods_reason_comment_not_empty",
                    "reason_comment is null or length(btrim(reason_comment)) > 0");
                table.HasCheckConstraint(
                    "ck_non_working_periods_status",
                    "status in ('active', 'canceled', 'corrected')");
            });

        builder.HasKey(period => period.Id);

        builder.HasAlternateKey(period => new
        {
            period.Id,
            period.StartDate,
            period.EndDate,
        })
            .HasName("AK_non_working_periods_id_range");

        builder.Property(period => period.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(period => period.StartDate)
            .HasColumnName("start_date")
            .HasColumnType("date");

        builder.Property(period => period.EndDate)
            .HasColumnName("end_date")
            .HasColumnType("date");

        builder.Property(period => period.ReasonCode)
            .HasColumnName("reason_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(period => period.ReasonComment)
            .HasColumnName("reason_comment")
            .HasMaxLength(1000);

        builder.Property(period => period.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(period => period.CreatedByAccountId)
            .HasColumnName("created_by_account_id");

        builder.Property(period => period.SessionId)
            .HasColumnName("session_id");

        builder.Property(period => period.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(period => period.CreatedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(period => period.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(period => new
        {
            period.Status,
            period.StartDate,
            period.EndDate,
        })
            .HasDatabaseName("ix_non_working_periods_status_range");

        builder.HasIndex(period => period.CreatedByAccountId)
            .HasDatabaseName("ix_non_working_periods_created_by_account_id");

        builder.HasIndex(period => period.SessionId)
            .HasDatabaseName("ix_non_working_periods_session_id");
    }
}
