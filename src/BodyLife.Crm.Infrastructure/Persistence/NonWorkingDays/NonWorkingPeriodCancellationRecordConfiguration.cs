using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodCancellationRecordConfiguration
    : IEntityTypeConfiguration<NonWorkingPeriodCancellationRecord>
{
    public void Configure(EntityTypeBuilder<NonWorkingPeriodCancellationRecord> builder)
    {
        builder.ToTable(
            "non_working_period_cancellations",
            table => table.HasCheckConstraint(
                "ck_non_working_period_cancellations_reason_not_empty",
                "length(btrim(reason)) > 0"));

        builder.HasKey(cancellation => cancellation.Id);

        builder.Property(cancellation => cancellation.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(cancellation => cancellation.NonWorkingPeriodId)
            .HasColumnName("non_working_period_id");

        builder.Property(cancellation => cancellation.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(cancellation => cancellation.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(cancellation => cancellation.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(cancellation => cancellation.SessionId)
            .HasColumnName("session_id");

        builder.HasOne(cancellation => cancellation.NonWorkingPeriod)
            .WithMany()
            .HasForeignKey(cancellation => cancellation.NonWorkingPeriodId)
            .HasConstraintName("FK_non_working_period_cancellations_period")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(cancellation => cancellation.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(cancellation => cancellation.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(cancellation => cancellation.NonWorkingPeriodId)
            .IsUnique()
            .HasDatabaseName("ux_non_working_period_cancellations_period_id");

        builder.HasIndex(cancellation => new
        {
            cancellation.RecordedAt,
            cancellation.NonWorkingPeriodId,
        })
            .IsDescending(true, false)
            .HasDatabaseName("ix_non_working_period_cancellations_timeline");

        builder.HasIndex(cancellation => cancellation.RecordedByAccountId)
            .HasDatabaseName("ix_non_working_period_cancellations_account_id");

        builder.HasIndex(cancellation => cancellation.SessionId)
            .HasDatabaseName("ix_non_working_period_cancellations_session_id");
    }
}
