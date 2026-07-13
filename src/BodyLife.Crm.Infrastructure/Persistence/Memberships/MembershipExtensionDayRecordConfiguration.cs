using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipExtensionDayRecordConfiguration
    : IEntityTypeConfiguration<MembershipExtensionDayRecord>
{
    public void Configure(EntityTypeBuilder<MembershipExtensionDayRecord> builder)
    {
        builder.ToTable(
            "membership_extension_days",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_membership_extension_days_source_type_not_empty",
                    "length(btrim(source_type)) > 0");
                table.HasCheckConstraint(
                    "ck_membership_extension_days_source_label_not_empty",
                    "length(btrim(source_label)) > 0");
            });

        builder.HasKey(extensionDay => extensionDay.Id);

        builder.Property(extensionDay => extensionDay.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(extensionDay => extensionDay.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(extensionDay => extensionDay.ExtensionDate)
            .HasColumnName("extension_date")
            .HasColumnType("date");

        builder.Property(extensionDay => extensionDay.SourceType)
            .HasColumnName("source_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(extensionDay => extensionDay.SourceId)
            .HasColumnName("source_id");

        builder.Property(extensionDay => extensionDay.SourceLabel)
            .HasColumnName("source_label")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(extensionDay => extensionDay.IsActive)
            .HasColumnName("is_active");

        builder.Property(extensionDay => extensionDay.RecalculatedAt)
            .HasColumnName("recalculated_at");

        builder.HasOne(extensionDay => extensionDay.Membership)
            .WithMany()
            .HasForeignKey(extensionDay => extensionDay.MembershipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(extensionDay => new
        {
            extensionDay.MembershipId,
            extensionDay.ExtensionDate,
            extensionDay.SourceType,
            extensionDay.SourceId,
        })
            .IsUnique()
            .HasDatabaseName("ux_membership_extension_days_membership_date_source");

        builder.HasIndex(extensionDay => new
        {
            extensionDay.MembershipId,
            extensionDay.ExtensionDate,
        })
            .HasFilter("is_active")
            .HasDatabaseName("ix_membership_extension_days_active_membership_date");

        builder.HasIndex(extensionDay => new
        {
            extensionDay.SourceType,
            extensionDay.SourceId,
        })
            .HasDatabaseName("ix_membership_extension_days_source");
    }
}
