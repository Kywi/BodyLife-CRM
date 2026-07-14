using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class VisitConsumptionRecordConfiguration
    : IEntityTypeConfiguration<VisitConsumptionRecord>
{
    public void Configure(EntityTypeBuilder<VisitConsumptionRecord> builder)
    {
        builder.ToTable(
            "visit_consumptions",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_visit_consumptions_visit_kind",
                    "visit_kind = 'membership'");
                table.HasCheckConstraint(
                    "ck_visit_consumptions_consumption_type",
                    "consumption_type = 'counted'");
                table.HasCheckConstraint(
                    "ck_visit_consumptions_source_fact_type",
                    "source_fact_type = 'visit'");
                table.HasCheckConstraint(
                    "ck_visit_consumptions_source_fact_identity",
                    "source_fact_id = visit_id");
                table.HasCheckConstraint(
                    "ck_visit_consumptions_status",
                    "status in ('active', 'canceled')");
            });

        builder.HasKey(consumption => consumption.Id);

        builder.Property(consumption => consumption.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(consumption => consumption.VisitId)
            .HasColumnName("visit_id");

        builder.Property(consumption => consumption.ClientId)
            .HasColumnName("client_id");

        builder.Property(consumption => consumption.VisitKind)
            .HasColumnName("visit_kind")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(consumption => consumption.MembershipId)
            .HasColumnName("membership_id");

        builder.Property(consumption => consumption.ConsumptionType)
            .HasColumnName("consumption_type")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(consumption => consumption.SourceFactType)
            .HasColumnName("source_fact_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(consumption => consumption.SourceFactId)
            .HasColumnName("source_fact_id");

        builder.Property(consumption => consumption.RecordedAt)
            .HasColumnName("recorded_at");

        builder.Property(consumption => consumption.RecordedByAccountId)
            .HasColumnName("recorded_by_account_id");

        builder.Property(consumption => consumption.RecordedSessionId)
            .HasColumnName("recorded_session_id");

        builder.Property(consumption => consumption.Status)
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.HasOne(consumption => consumption.Visit)
            .WithMany()
            .HasForeignKey(consumption => new
            {
                consumption.VisitId,
                consumption.ClientId,
                consumption.VisitKind,
            })
            .HasPrincipalKey(visit => new
            {
                visit.Id,
                visit.ClientId,
                visit.VisitKind,
            })
            .HasConstraintName("FK_visit_consumptions_visits_visit_client_kind")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(consumption => consumption.Membership)
            .WithMany()
            .HasForeignKey(consumption => new
            {
                consumption.MembershipId,
                consumption.ClientId,
            })
            .HasPrincipalKey(membership => new
            {
                membership.Id,
                membership.ClientId,
            })
            .HasConstraintName(
                "FK_visit_consumptions_issued_memberships_membership_client")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AccountRecord>()
            .WithMany()
            .HasForeignKey(consumption => consumption.RecordedByAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<SessionRecord>()
            .WithMany()
            .HasForeignKey(consumption => consumption.RecordedSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(consumption => new
        {
            consumption.VisitId,
            consumption.ClientId,
            consumption.VisitKind,
        })
            .HasDatabaseName("ix_visit_consumptions_visit_client_kind");

        builder.HasIndex(consumption => consumption.VisitId)
            .IsUnique()
            .HasFilter("status = 'active' and consumption_type = 'counted'")
            .HasDatabaseName("ux_visit_consumptions_active_counted_visit");

        builder.HasIndex(consumption => new
        {
            consumption.MembershipId,
            consumption.ClientId,
        })
            .HasDatabaseName("ix_visit_consumptions_membership_client");

        builder.HasIndex(consumption => new
        {
            consumption.MembershipId,
            consumption.Status,
            consumption.RecordedAt,
            consumption.VisitId,
        })
            .HasDatabaseName("ix_visit_consumptions_membership_recalculation");

        builder.HasIndex(consumption => consumption.RecordedByAccountId)
            .HasDatabaseName("ix_visit_consumptions_recorded_by_account_id");

        builder.HasIndex(consumption => consumption.RecordedSessionId)
            .HasDatabaseName("ix_visit_consumptions_recorded_session_id");
    }
}
