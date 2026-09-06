using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipLifecycleClosureRecordConfiguration
    : IEntityTypeConfiguration<MembershipLifecycleClosureRecord>
{
    public void Configure(EntityTypeBuilder<MembershipLifecycleClosureRecord> builder)
    {
        builder.ToTable("membership_lifecycle_closures", table =>
        {
            table.HasCheckConstraint("ck_membership_lifecycle_closures_reason", "reason_code in ('zero_balance_rollover', 'negative_balance_rollover', 'one_off_zero_balance')");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_shape", "(reason_code in ('zero_balance_rollover', 'negative_balance_rollover') and successor_membership_id is not null and negative_closure_id is null) or (reason_code = 'one_off_zero_balance' and successor_membership_id is null and negative_closure_id is not null)");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_distinct_memberships", "successor_membership_id is null or successor_membership_id <> source_membership_id");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_correlation", "length(btrim(correlation_id)) > 0");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_idempotency", "length(btrim(idempotency_key)) > 0");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_origin", "entry_origin in ('normal', 'manual_backfill', 'paper_fallback', 'future_import')");
            table.HasCheckConstraint("ck_membership_lifecycle_closures_explanation", "explanation is null or length(btrim(explanation)) > 0");
        });
        builder.HasKey(closure => closure.Id);
        builder.Property(closure => closure.Id).ValueGeneratedNever().HasColumnName("id");
        builder.Property(closure => closure.ClientId).HasColumnName("client_id");
        builder.Property(closure => closure.SourceMembershipId).HasColumnName("source_membership_id");
        builder.Property(closure => closure.SuccessorMembershipId).HasColumnName("successor_membership_id");
        builder.Property(closure => closure.NegativeClosureId).HasColumnName("negative_closure_id");
        builder.Property(closure => closure.ReasonCode).HasColumnName("reason_code").HasMaxLength(64).IsRequired();
        builder.Property(closure => closure.RecordedByAccountId).HasColumnName("recorded_by_account_id");
        builder.Property(closure => closure.SessionId).HasColumnName("session_id");
        builder.Property(closure => closure.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128).IsRequired();
        builder.Property(closure => closure.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200).IsRequired();
        builder.Property(closure => closure.EntryOrigin).HasColumnName("entry_origin").HasMaxLength(32).IsRequired();
        builder.Property(closure => closure.EntryBatchId).HasColumnName("entry_batch_id");
        builder.Property(closure => closure.OccurredAt).HasColumnName("occurred_at");
        builder.Property(closure => closure.RecordedAt).HasColumnName("recorded_at");
        builder.Property(closure => closure.Explanation).HasColumnName("explanation").HasMaxLength(2000);
        builder.HasOne<ClientRecord>().WithMany().HasForeignKey(closure => closure.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>().WithMany().HasForeignKey(closure => new { closure.SourceMembershipId, closure.ClientId }).HasPrincipalKey(membership => new { membership.Id, membership.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<IssuedMembershipRecord>().WithMany().HasForeignKey(closure => new { closure.SuccessorMembershipId, closure.ClientId }).HasPrincipalKey(membership => new { membership.Id, membership.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MembershipNegativeClosureRecord>().WithMany().HasForeignKey(closure => new { closure.NegativeClosureId, closure.ClientId }).HasPrincipalKey(negativeClosure => new { negativeClosure.Id, negativeClosure.ClientId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AccountRecord>().WithMany().HasForeignKey(closure => closure.RecordedByAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SessionRecord>().WithMany().HasForeignKey(closure => closure.SessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(closure => closure.SourceMembershipId).IsUnique().HasDatabaseName("ux_membership_lifecycle_closures_source_membership");
        builder.HasIndex(closure => new { closure.ClientId, closure.RecordedAt }).HasDatabaseName("ix_membership_lifecycle_closures_client_timeline");
        builder.HasIndex(closure => closure.SuccessorMembershipId).HasDatabaseName("ix_membership_lifecycle_closures_successor_membership");
        builder.HasIndex(closure => closure.NegativeClosureId).IsUnique().HasFilter("negative_closure_id is not null").HasDatabaseName("ux_membership_lifecycle_closures_negative_closure");
    }
}
