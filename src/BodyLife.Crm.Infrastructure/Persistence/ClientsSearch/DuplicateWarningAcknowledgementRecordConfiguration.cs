using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class DuplicateWarningAcknowledgementRecordConfiguration
    : IEntityTypeConfiguration<DuplicateWarningAcknowledgementRecord>
{
    public void Configure(EntityTypeBuilder<DuplicateWarningAcknowledgementRecord> builder)
    {
        builder.ToTable(
            "duplicate_warning_acknowledgements",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_duplicate_warning_acknowledgements_warning_type",
                    "warning_type in ('duplicate_phone', 'similar_name')");
                table.HasCheckConstraint(
                    "ck_duplicate_warning_acknowledgements_distinct_clients",
                    "client_id <> matched_client_id");
                table.HasCheckConstraint(
                    "ck_duplicate_warning_acknowledgements_reason_not_empty",
                    "length(btrim(reason)) > 0");
            });

        builder.HasKey(acknowledgement => acknowledgement.Id);

        builder.Property(acknowledgement => acknowledgement.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(acknowledgement => acknowledgement.ClientId)
            .HasColumnName("client_id");

        builder.Property(acknowledgement => acknowledgement.WarningType)
            .HasColumnName("warning_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(acknowledgement => acknowledgement.MatchedClientId)
            .HasColumnName("matched_client_id");

        builder.Property(acknowledgement => acknowledgement.AcknowledgedByAccountId)
            .HasColumnName("acknowledged_by_account_id");

        builder.Property(acknowledgement => acknowledgement.AcknowledgedAt)
            .HasColumnName("acknowledged_at");

        builder.Property(acknowledgement => acknowledgement.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.HasOne<ClientRecord>()
            .WithMany()
            .HasForeignKey(acknowledgement => acknowledgement.ClientId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_duplicate_warning_acks_client");

        builder.HasOne<ClientRecord>()
            .WithMany()
            .HasForeignKey(acknowledgement => acknowledgement.MatchedClientId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_duplicate_warning_acks_matched_client");

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(acknowledgement => acknowledgement.AcknowledgedByAccountId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_duplicate_warning_acks_actor");

        builder.HasIndex(acknowledgement => new
        {
            acknowledgement.ClientId,
            acknowledgement.AcknowledgedAt,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_duplicate_warning_acks_client_timeline");

        builder.HasIndex(acknowledgement => new
        {
            acknowledgement.MatchedClientId,
            acknowledgement.AcknowledgedAt,
        })
            .IsDescending(false, true)
            .HasDatabaseName("ix_duplicate_warning_acks_match_timeline");

        builder.HasIndex(acknowledgement => acknowledgement.AcknowledgedByAccountId)
            .HasDatabaseName("ix_duplicate_warning_acks_actor");
    }
}
