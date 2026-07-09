using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class AccountRecordConfiguration : IEntityTypeConfiguration<AccountRecord>
{
    public void Configure(EntityTypeBuilder<AccountRecord> builder)
    {
        builder.ToTable(
            "accounts",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_accounts_display_name_not_empty",
                    "length(btrim(display_name)) > 0");
                table.HasCheckConstraint(
                    "ck_accounts_account_type",
                    "account_type in ('owner', 'named_admin', 'shared_reception_admin')");
                table.HasCheckConstraint(
                    "ck_accounts_role",
                    "role in ('owner', 'admin')");
                table.HasCheckConstraint(
                    "ck_accounts_account_type_role",
                    """
                    (account_type = 'owner' and role = 'owner')
                    or (account_type in ('named_admin', 'shared_reception_admin') and role = 'admin')
                    """);
                table.HasCheckConstraint(
                    "ck_accounts_deactivated_at_after_created",
                    "deactivated_at is null or deactivated_at >= created_at");
                table.HasCheckConstraint(
                    "ck_accounts_active_deactivated_at",
                    "(is_active and deactivated_at is null) or (not is_active and deactivated_at is not null)");
            });

        builder.HasKey(account => account.Id);

        builder.Property(account => account.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(account => account.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(account => account.AccountType)
            .HasColumnName("account_type")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(account => account.Role)
            .HasColumnName("role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(account => account.IsActive)
            .HasColumnName("is_active");

        builder.Property(account => account.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(account => account.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.HasIndex(account => account.AccountType)
            .IsUnique()
            .HasFilter("account_type = 'owner'")
            .HasDatabaseName("ux_accounts_single_owner");

        builder.HasIndex(account => new { account.IsActive, account.AccountType })
            .HasDatabaseName("ix_accounts_active_type");
    }
}
