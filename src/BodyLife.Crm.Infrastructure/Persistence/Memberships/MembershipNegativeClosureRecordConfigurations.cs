using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipNegativeClosureRecordConfiguration
    : IEntityTypeConfiguration<MembershipNegativeClosureRecord>
{
    public void Configure(EntityTypeBuilder<MembershipNegativeClosureRecord> builder)
    {
        builder.ToTable(
            "membership_negative_closures",
            table =>
            {
                table.HasCheckConstraint(
                    "ck_negative_closures_type",
                    "closure_type in ('one_off', 'new_membership')");
                table.HasCheckConstraint(
                    "ck_negative_closures_visits_count",
                    "visits_count > 0");
                table.HasCheckConstraint(
                    "ck_negative_closures_comment_not_empty",
                    "comment is null or length(btrim(comment)) > 0");
                table.HasCheckConstraint(
                    "ck_negative_closures_origin",
                    "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
                table.HasCheckConstraint(
                    "ck_negative_closures_status",
                    "status in ('active', 'canceled', 'replaced')");
                table.HasCheckConstraint(
                    "ck_negative_closures_covering_shape",
                    "(closure_type = 'one_off' and covering_membership_id is null) or (closure_type = 'new_membership' and covering_membership_id is not null)");
            });

        builder.HasKey(closure => closure.Id);
        builder.HasAlternateKey(closure => new { closure.Id, closure.ClientId });
        builder.Property(closure => closure.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(closure => closure.ClientId).HasColumnName("client_id");
        builder.Property(closure => closure.ClosureType).HasColumnName("closure_type").HasMaxLength(32).IsRequired();
        builder.Property(closure => closure.CoveringMembershipId).HasColumnName("covering_membership_id");
        builder.Property(closure => closure.OldestOpenNegativeVisitId).HasColumnName("oldest_open_negative_visit_id");
        builder.Property(closure => closure.VisitsCount).HasColumnName("visits_count");
        builder.Property(closure => closure.Comment).HasColumnName("comment").HasMaxLength(1000);
        builder.Property(closure => closure.OccurredAt).HasColumnName("occurred_at");
        builder.Property(closure => closure.RecordedAt).HasColumnName("recorded_at");
        builder.Property(closure => closure.RecordedByAccountId).HasColumnName("recorded_by_account_id");
        builder.Property(closure => closure.SessionId).HasColumnName("session_id");
        builder.Property(closure => closure.EntryOrigin).HasColumnName("entry_origin").HasMaxLength(32).IsRequired();
        builder.Property(closure => closure.EntryBatchId).HasColumnName("entry_batch_id");
        builder.Property(closure => closure.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(closure => closure.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.HasOne<ClientRecord>().WithMany().HasForeignKey(closure => closure.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VisitRecord>()
            .WithMany()
            .HasForeignKey(closure => new
            {
                closure.OldestOpenNegativeVisitId,
                closure.ClientId,
            })
            .HasPrincipalKey(visit => new
            {
                visit.Id,
                visit.ClientId,
            })
            .HasConstraintName("FK_negative_closures_oldest_visit_client")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>().WithMany().HasForeignKey(closure => new { closure.CoveringMembershipId, closure.ClientId }).HasPrincipalKey(membership => new { membership.Id, membership.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(closure => closure.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SessionRecord>().WithMany().HasForeignKey(closure => closure.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(closure => new { closure.ClientId, closure.RecordedAt }).HasDatabaseName("ix_negative_closures_client_timeline");
        builder.HasIndex(closure => closure.IdempotencyKey).IsUnique().HasDatabaseName("ux_negative_closures_idempotency_key");
    }
}

internal sealed class MembershipNegativeClosureLineRecordConfiguration
    : IEntityTypeConfiguration<MembershipNegativeClosureLineRecord>
{
    public void Configure(EntityTypeBuilder<MembershipNegativeClosureLineRecord> builder)
    {
        builder.ToTable("membership_negative_closure_lines", table =>
        {
            table.HasCheckConstraint("ck_negative_closure_lines_quantity", "quantity > 0");
            table.HasCheckConstraint("ck_negative_closure_lines_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_negative_closure_lines_snapshots", "length(btrim(type_name_snapshot)) > 0 and duration_days_snapshot > 0 and visits_limit_snapshot = 1 and unit_price_amount_snapshot > 0 and line_total = quantity * unit_price_amount_snapshot and length(btrim(currency_snapshot)) > 0 and currency_snapshot = upper(btrim(currency_snapshot))");
        });
        builder.HasKey(line => line.Id);
        builder.HasAlternateKey(line => new
        {
            line.Id,
            line.NegativeClosureId,
        })
            .HasName("AK_negative_closure_lines_id_closure_id");
        builder.Property(line => line.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(line => line.NegativeClosureId).HasColumnName("negative_closure_id");
        builder.Property(line => line.MembershipTypeId).HasColumnName("membership_type_id");
        builder.Property(line => line.TypeNameSnapshot).HasColumnName("type_name_snapshot").HasMaxLength(200).IsRequired();
        builder.Property(line => line.DurationDaysSnapshot).HasColumnName("duration_days_snapshot");
        builder.Property(line => line.VisitsLimitSnapshot).HasColumnName("visits_limit_snapshot");
        builder.Property(line => line.Quantity).HasColumnName("quantity");
        builder.Property(line => line.UnitPriceAmountSnapshot).HasColumnName("unit_price_amount_snapshot").HasColumnType("numeric");
        builder.Property(line => line.CurrencySnapshot).HasColumnName("currency_snapshot").HasMaxLength(3).IsRequired();
        builder.Property(line => line.LineTotal).HasColumnName("line_total").HasColumnType("numeric");
        builder.Property(line => line.Sequence).HasColumnName("sequence");
        builder.HasOne<MembershipNegativeClosureRecord>().WithMany().HasForeignKey(line => line.NegativeClosureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipTypeRecord>().WithMany().HasForeignKey(line => line.MembershipTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(line => new { line.NegativeClosureId, line.Sequence }).IsUnique().HasDatabaseName("ux_negative_closure_lines_sequence");
    }
}

internal sealed class MembershipNegativeClosureItemRecordConfiguration
    : IEntityTypeConfiguration<MembershipNegativeClosureItemRecord>
{
    public void Configure(EntityTypeBuilder<MembershipNegativeClosureItemRecord> builder)
    {
        builder.ToTable("membership_negative_closure_items", table =>
        {
            table.HasCheckConstraint("ck_negative_closure_items_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_negative_closure_items_status", "status in ('active', 'canceled', 'replaced')");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(item => item.NegativeClosureId).HasColumnName("negative_closure_id");
        builder.Property(item => item.ClientId).HasColumnName("client_id");
        builder.Property(item => item.ClosureLineId).HasColumnName("closure_line_id");
        builder.Property(item => item.Sequence).HasColumnName("sequence");
        builder.Property(item => item.VisitId).HasColumnName("visit_id");
        builder.Property(item => item.SourceMembershipId).HasColumnName("source_membership_id");
        builder.Property(item => item.OldConsumptionId).HasColumnName("old_consumption_id");
        builder.Property(item => item.CoveringMembershipId).HasColumnName("covering_membership_id");
        builder.Property(item => item.NewConsumptionId).HasColumnName("new_consumption_id");
        builder.Property(item => item.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.HasOne<MembershipNegativeClosureRecord>().WithMany().HasForeignKey(item => new { item.NegativeClosureId, item.ClientId }).HasPrincipalKey(closure => new { closure.Id, closure.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipNegativeClosureLineRecord>()
            .WithMany()
            .HasForeignKey(item => new
            {
                item.ClosureLineId,
                item.NegativeClosureId,
            })
            .HasPrincipalKey(line => new
            {
                line.Id,
                line.NegativeClosureId,
            })
            .HasConstraintName("FK_negative_closure_items_line_closure")
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VisitRecord>().WithMany().HasForeignKey(item => new { item.VisitId, item.ClientId }).HasPrincipalKey(visit => new { visit.Id, visit.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>().WithMany().HasForeignKey(item => new { item.SourceMembershipId, item.ClientId }).HasPrincipalKey(membership => new { membership.Id, membership.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>().WithMany().HasForeignKey(item => new { item.CoveringMembershipId, item.ClientId }).HasPrincipalKey(membership => new { membership.Id, membership.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VisitConsumptionRecord>().WithMany().HasForeignKey(item => new { item.OldConsumptionId, item.ClientId }).HasPrincipalKey(consumption => new { consumption.Id, consumption.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<VisitConsumptionRecord>().WithMany().HasForeignKey(item => new { item.NewConsumptionId, item.ClientId }).HasPrincipalKey(consumption => new { consumption.Id, consumption.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.NegativeClosureId, item.Sequence }).IsUnique().HasDatabaseName("ux_negative_closure_items_sequence");
        builder.HasIndex(item => item.VisitId).IsUnique().HasFilter("status = 'active'").HasDatabaseName("ux_negative_closure_items_active_visit");
        builder.HasIndex(item => item.NewConsumptionId).IsUnique().HasFilter("new_consumption_id is not null").HasDatabaseName("ux_negative_closure_items_new_consumption");
        builder.HasIndex(item => item.SourceMembershipId).HasDatabaseName("ix_negative_closure_items_source_membership");
        builder.HasIndex(item => item.CoveringMembershipId).HasDatabaseName("ix_negative_closure_items_covering_membership");
        builder.HasIndex(item => item.VisitId).HasDatabaseName("ix_negative_closure_items_visit");
    }
}

internal sealed class MembershipNegativeClosureCorrectionRecordConfiguration
    : IEntityTypeConfiguration<MembershipNegativeClosureCorrectionRecord>
{
    public void Configure(EntityTypeBuilder<MembershipNegativeClosureCorrectionRecord> builder)
    {
        builder.ToTable("membership_negative_closure_corrections", table =>
        {
            table.HasCheckConstraint("ck_negative_closure_corrections_mode", "mode in ('cancel', 'replace')");
            table.HasCheckConstraint("ck_negative_closure_corrections_shape", "(mode = 'cancel' and replacement_closure_id is null) or (mode = 'replace' and replacement_closure_id is not null and replacement_closure_id <> original_closure_id)");
            table.HasCheckConstraint("ck_negative_closure_corrections_reason", "length(btrim(reason)) > 0");
            table.HasCheckConstraint("ck_negative_closure_corrections_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
        });
        builder.HasKey(correction => correction.Id);
        builder.Property(correction => correction.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(correction => correction.OriginalClosureId).HasColumnName("original_closure_id");
        builder.Property(correction => correction.ReplacementClosureId).HasColumnName("replacement_closure_id");
        builder.Property(correction => correction.Mode).HasColumnName("mode").HasMaxLength(32).IsRequired();
        builder.Property(correction => correction.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(correction => correction.OccurredAt).HasColumnName("occurred_at");
        builder.Property(correction => correction.RecordedAt).HasColumnName("recorded_at");
        builder.Property(correction => correction.RecordedByAccountId).HasColumnName("recorded_by_account_id");
        builder.Property(correction => correction.SessionId).HasColumnName("session_id");
        builder.Property(correction => correction.EntryOrigin).HasColumnName("entry_origin").HasMaxLength(32).IsRequired();
        builder.Property(correction => correction.EntryBatchId).HasColumnName("entry_batch_id");
        builder.Property(correction => correction.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.HasOne<MembershipNegativeClosureRecord>().WithMany().HasForeignKey(correction => correction.OriginalClosureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipNegativeClosureRecord>().WithMany().HasForeignKey(correction => correction.ReplacementClosureId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(correction => correction.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SessionRecord>().WithMany().HasForeignKey(correction => correction.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(correction => correction.OriginalClosureId).IsUnique().HasDatabaseName("ux_negative_closure_corrections_original");
        builder.HasIndex(correction => correction.ReplacementClosureId).IsUnique().HasFilter("replacement_closure_id is not null").HasDatabaseName("ux_negative_closure_corrections_replacement");
        builder.HasIndex(correction => correction.IdempotencyKey).IsUnique().HasDatabaseName("ux_negative_closure_corrections_idempotency_key");
    }
}
