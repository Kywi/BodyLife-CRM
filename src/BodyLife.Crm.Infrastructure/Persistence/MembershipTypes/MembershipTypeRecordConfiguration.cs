using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

internal sealed class MembershipTypeRecordConfiguration
    : IEntityTypeConfiguration<MembershipTypeRecord>
{
    public void Configure(EntityTypeBuilder<MembershipTypeRecord> builder)
    {
        builder.ToTable(
            "membership_types",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_types_name_not_empty",
                    "length(btrim(name)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_types_duration_positive",
                    "duration_days > 0");
                table.HasCheckConstraint(
                    "ck_membership_types_visits_non_negative",
                    "visits_limit >= 0");
                table.HasCheckConstraint(
                    "ck_membership_types_price_non_negative",
                    "price_amount >= 0");
                table.HasCheckConstraint(
                    "ck_membership_types_currency_canonical",
                    "length(btrim(price_currency)) > 0 and price_currency = upper(btrim(price_currency))");
                table.HasCheckConstraint(
                    "ck_membership_types_comment_not_empty",
                    "comment is null or length(btrim(comment)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_types_updated_after_created",
                    "updated_at >= created_at");
                table.HasCheckConstraint(
                    "ck_membership_types_lifecycle",
                    """
                    (
                        is_active
                        and deactivated_at is null
                    )
                    or (
                        not is_active
                        and deactivated_at is not null
                        and deactivated_at >= created_at
                        and deactivated_at <= updated_at
                    )
                    """);
            });

        builder.HasKey(membershipType => membershipType.Id);

        builder.Property(membershipType => membershipType.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(membershipType => membershipType.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(membershipType => membershipType.DurationDays)
            .HasColumnName("duration_days");

        builder.Property(membershipType => membershipType.VisitsLimit)
            .HasColumnName("visits_limit");

        builder.Property(membershipType => membershipType.PriceAmount)
            .HasColumnName("price_amount")
            .HasColumnType("numeric");

        builder.Property(membershipType => membershipType.PriceCurrency)
            .HasColumnName("price_currency")
            .IsRequired();

        builder.Property(membershipType => membershipType.IsActive)
            .HasColumnName("is_active");

        builder.Property(membershipType => membershipType.Comment)
            .HasColumnName("comment");

        builder.Property(membershipType => membershipType.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(membershipType => membershipType.UpdatedAt)
            .HasColumnName("updated_at");

        builder.Property(membershipType => membershipType.DeactivatedAt)
            .HasColumnName("deactivated_at");

        builder.HasIndex(membershipType => new { membershipType.Name, membershipType.Id })
            .HasFilter("is_active")
            .HasDatabaseName("ix_membership_types_active_issue_order");
    }
}
