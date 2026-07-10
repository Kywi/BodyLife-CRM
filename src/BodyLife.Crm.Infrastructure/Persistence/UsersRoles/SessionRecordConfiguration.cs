using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class SessionRecordConfiguration : IEntityTypeConfiguration<SessionRecord>
{
    public void Configure(EntityTypeBuilder<SessionRecord> builder)
    {
        builder.ToTable(
            "sessions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_sessions_last_seen_after_started",
                    "last_seen_at >= started_at");
                table.HasCheckConstraint(
                    "ck_sessions_ended_at_after_started",
                    "ended_at is null or ended_at >= started_at");
                table.HasCheckConstraint(
                    "ck_sessions_expires_after_started",
                    "expires_at > started_at");
            });

        builder.HasKey(session => session.Id);

        builder.Property(session => session.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(session => session.AccountId)
            .HasColumnName("account_id");

        builder.Property(session => session.DeviceLabel)
            .HasColumnName("device_label")
            .HasMaxLength(120);

        builder.Property(session => session.StartedAt)
            .HasColumnName("started_at");

        builder.Property(session => session.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(session => session.EndedAt)
            .HasColumnName("ended_at");

        builder.Property(session => session.LastSeenAt)
            .HasColumnName("last_seen_at");

        builder.HasOne(session => session.Account)
            .WithMany()
            .HasForeignKey(session => session.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => new { session.AccountId, session.ExpiresAt })
            .HasFilter("ended_at is null")
            .HasDatabaseName("ix_sessions_active_account_expires_at");
    }
}
