using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class ClientCardAssignmentRecordConfiguration
    : IEntityTypeConfiguration<ClientCardAssignmentRecord>
{
    public void Configure(EntityTypeBuilder<ClientCardAssignmentRecord> builder)
    {
        builder.ToTable(
            "client_card_assignments",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_client_card_assignments_raw_not_empty",
                    "length(btrim(card_number_raw)) > 0");
                table.HasCheckConstraint(
                    "ck_client_card_assignments_normalized_not_empty",
                    "length(btrim(card_number_normalized)) > 0");
                table.HasCheckConstraint(
                    "ck_client_card_assignments_ended_after_assigned",
                    "ended_at is null or ended_at >= assigned_at");
                table.HasCheckConstraint(
                    "ck_client_card_assignments_lifecycle",
                    """
                    (
                        is_current
                        and ended_at is null
                        and ended_by_account_id is null
                        and end_reason is null
                    )
                    or (
                        not is_current
                        and ended_at is not null
                        and ended_by_account_id is not null
                        and end_reason is not null
                        and length(btrim(end_reason)) > 0
                    )
                    """);
            });

        builder.HasKey(assignment => assignment.Id);

        builder.Property(assignment => assignment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(assignment => assignment.ClientId)
            .HasColumnName("client_id");

        builder.Property(assignment => assignment.CardNumberRaw)
            .HasColumnName("card_number_raw")
            .IsRequired();

        builder.Property(assignment => assignment.CardNumberNormalized)
            .HasColumnName("card_number_normalized")
            .IsRequired();

        builder.Property(assignment => assignment.AssignedAt)
            .HasColumnName("assigned_at");

        builder.Property(assignment => assignment.AssignedByAccountId)
            .HasColumnName("assigned_by_account_id");

        builder.Property(assignment => assignment.EndedAt)
            .HasColumnName("ended_at");

        builder.Property(assignment => assignment.EndedByAccountId)
            .HasColumnName("ended_by_account_id");

        builder.Property(assignment => assignment.EndReason)
            .HasColumnName("end_reason");

        builder.Property(assignment => assignment.IsCurrent)
            .HasColumnName("is_current");

        builder.HasOne<ClientRecord>()
            .WithMany()
            .HasForeignKey(assignment => assignment.ClientId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(assignment => assignment.AssignedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(assignment => assignment.EndedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(assignment => assignment.AssignedByAccountId)
            .HasDatabaseName("ix_client_card_assignments_assigned_by_account_id");

        builder.HasIndex(assignment => assignment.EndedByAccountId)
            .HasDatabaseName("ix_client_card_assignments_ended_by_account_id");

        builder.HasIndex(assignment => new { assignment.ClientId, assignment.AssignedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_client_card_assignments_client_history");

        builder.HasIndex(assignment => assignment.CardNumberNormalized)
            .IsUnique()
            .HasFilter("is_current")
            .HasDatabaseName("ux_client_card_assignments_current_card");

        builder.HasIndex(assignment => assignment.ClientId)
            .IsUnique()
            .HasFilter("is_current")
            .HasDatabaseName("ux_client_card_assignments_current_client");
    }
}
