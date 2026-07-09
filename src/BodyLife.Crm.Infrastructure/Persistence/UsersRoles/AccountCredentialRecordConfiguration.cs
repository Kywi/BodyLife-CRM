using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

internal sealed class AccountCredentialRecordConfiguration : IEntityTypeConfiguration<AccountCredentialRecord>
{
    public void Configure(EntityTypeBuilder<AccountCredentialRecord> builder)
    {
        builder.ToTable(
            "account_credentials",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_account_credentials_login_name_not_empty",
                    "length(btrim(login_name)) > 0");
                table.HasCheckConstraint(
                    "ck_account_credentials_normalized_login_name_not_empty",
                    "length(btrim(normalized_login_name)) > 0");
                table.HasCheckConstraint(
                    "ck_account_credentials_password_hash_not_empty",
                    "length(btrim(password_hash)) > 0");
            });

        builder.HasKey(credential => credential.AccountId);

        builder.Property(credential => credential.AccountId)
            .HasColumnName("account_id")
            .ValueGeneratedNever();

        builder.Property(credential => credential.LoginName)
            .HasColumnName("login_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(credential => credential.NormalizedLoginName)
            .HasColumnName("normalized_login_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(credential => credential.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(credential => credential.PasswordChangedAt)
            .HasColumnName("password_changed_at");

        builder.HasOne(credential => credential.Account)
            .WithOne()
            .HasForeignKey<AccountCredentialRecord>(credential => credential.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(credential => credential.NormalizedLoginName)
            .IsUnique()
            .HasDatabaseName("ux_account_credentials_normalized_login_name");
    }
}
