using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class ClientRecordConfiguration : IEntityTypeConfiguration<ClientRecord>
{
    public void Configure(EntityTypeBuilder<ClientRecord> builder)
    {
        builder.ToTable(
            "clients",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_clients_surname_not_empty",
                    "length(btrim(surname)) > 0");
                table.HasCheckConstraint(
                    "ck_clients_name_not_empty",
                    "length(btrim(name)) > 0");
                table.HasCheckConstraint(
                    "ck_clients_patronymic_not_empty",
                    "patronymic is null or length(btrim(patronymic)) > 0");
                table.HasCheckConstraint(
                    "ck_clients_normalized_full_name_not_empty",
                    "length(btrim(normalized_full_name)) > 0");
                table.HasCheckConstraint(
                    "ck_clients_phone_fields_consistent",
                    """
                    (phone_raw is null and phone_normalized is null and phone_last4 is null)
                    or (
                        phone_raw is not null
                        and length(btrim(phone_raw)) > 0
                        and phone_normalized is not null
                        and phone_normalized ~ '^[0-9]{4,}$'
                        and phone_last4 is not null
                        and phone_last4 ~ '^[0-9]{4}$'
                        and phone_last4 = right(phone_normalized, 4)
                    )
                    """);
                table.HasCheckConstraint(
                    "ck_clients_operational_status",
                    "operational_status in ('active', 'inactive')");
                table.HasCheckConstraint(
                    "ck_clients_updated_after_created",
                    "updated_at >= created_at");
            });

        builder.HasKey(client => client.Id);

        builder.Property(client => client.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(client => client.Surname)
            .HasColumnName("surname")
            .IsRequired();

        builder.Property(client => client.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(client => client.Patronymic)
            .HasColumnName("patronymic");

        builder.Property(client => client.NormalizedFullName)
            .HasColumnName("normalized_full_name")
            .IsRequired();

        builder.Property(client => client.PhoneRaw)
            .HasColumnName("phone_raw");

        builder.Property(client => client.PhoneNormalized)
            .HasColumnName("phone_normalized");

        builder.Property(client => client.PhoneLastFour)
            .HasColumnName("phone_last4");

        builder.Property(client => client.Comment)
            .HasColumnName("comment");

        builder.Property(client => client.OperationalStatus)
            .HasColumnName("operational_status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(client => client.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(client => client.CreatedByAccountId)
            .HasColumnName("created_by_account_id");

        builder.Property(client => client.UpdatedAt)
            .HasColumnName("updated_at");

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(client => client.CreatedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(client => client.CreatedByAccountId)
            .HasDatabaseName("ix_clients_created_by_account_id");

        builder.HasIndex(client => client.NormalizedFullName)
            .HasDatabaseName("ix_clients_normalized_full_name");

        builder.HasIndex(client => client.PhoneNormalized)
            .HasFilter("phone_normalized is not null")
            .HasDatabaseName("ix_clients_phone_normalized");

        builder.HasIndex(client => new { client.PhoneLastFour, client.OperationalStatus })
            .HasFilter("phone_last4 is not null")
            .HasDatabaseName("ix_clients_phone_last4_status");
    }
}
