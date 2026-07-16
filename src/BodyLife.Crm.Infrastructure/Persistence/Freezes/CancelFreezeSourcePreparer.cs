using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

public sealed class CancelFreezeSourcePreparer
{
    private readonly BodyLifeDbContext dbContext;

    public CancelFreezeSourcePreparer(BodyLifeDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        this.dbContext = dbContext;
    }

    public async Task<CancelFreezeSourcePreparationResult> PrepareAsync(
        Guid freezeId,
        CancellationToken cancellationToken = default)
    {
        if (freezeId == Guid.Empty)
        {
            throw new ArgumentException("Freeze id is required.", nameof(freezeId));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "CancelFreeze source preparation requires a caller-owned database "
                + "transaction so the Membership is locked before Freeze sources.");
        }

        var membershipRows = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    issued.id,
                    issued.client_id,
                    issued.membership_type_id,
                    issued.type_name_snapshot,
                    issued.duration_days_snapshot,
                    issued.visits_limit_snapshot,
                    issued.price_amount_snapshot,
                    issued.price_currency_snapshot,
                    issued.start_date,
                    issued.base_end_date,
                    issued.issued_at,
                    issued.issued_by_account_id,
                    issued.status,
                    issued.entry_origin,
                    issued.entry_batch_id,
                    issued.comment
                from bodylife.issued_memberships as issued
                inner join bodylife.freezes as source_freeze
                    on source_freeze.membership_id = issued.id
                    and source_freeze.client_id = issued.client_id
                where source_freeze.id = {freezeId}
                for update of issued
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var membership = membershipRows.SingleOrDefault();
        if (membership is null)
        {
            return CancelFreezeSourcePreparationResult.NotFound(freezeId);
        }

        var freezeRows = await dbContext.Set<FreezeRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    membership_id,
                    start_date,
                    end_date,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id,
                    status
                from bodylife.freezes
                where id = {freezeId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var freeze = freezeRows.SingleOrDefault();
        if (freeze is null
            || freeze.ClientId != membership.ClientId
            || freeze.MembershipId != membership.Id)
        {
            return CancelFreezeSourcePreparationResult.InconsistentSource(freezeId);
        }

        var cancellations = await dbContext.Set<FreezeCancellationRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    freeze_id,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id
                from bodylife.freeze_cancellations
                where freeze_id = {freezeId}
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        if (cancellations.Length > 1)
        {
            return CancelFreezeSourcePreparationResult.InconsistentSource(freezeId);
        }

        var source = new FreezeCancellationSource(
            freeze.Id,
            freeze.ClientId,
            freeze.MembershipId,
            new DateRange(freeze.StartDate, freeze.EndDate),
            freeze.Reason,
            freeze.OccurredAt,
            freeze.RecordedAt,
            freeze.RecordedByAccountId,
            freeze.SessionId,
            MapEntryOrigin(freeze.EntryOrigin),
            freeze.EntryBatchId,
            MapStatus(freeze.Status),
            cancellations.SingleOrDefault()?.Id);

        if (source.Status == FreezeCancellationSourceStatus.Canceled
            || source.ExistingCancellationId is not null)
        {
            return CancelFreezeSourcePreparationResult.AlreadyCanceled(source);
        }

        return CancelFreezeSourcePreparationResult.Prepared(source);
    }

    private static EntryOrigin MapEntryOrigin(string entryOrigin)
    {
        return entryOrigin switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => throw new InvalidOperationException(
                $"Freeze entry origin '{entryOrigin}' is not supported."),
        };
    }

    private static FreezeCancellationSourceStatus MapStatus(string status)
    {
        return status switch
        {
            "active" => FreezeCancellationSourceStatus.Active,
            "canceled" => FreezeCancellationSourceStatus.Canceled,
            _ => throw new InvalidOperationException(
                $"Freeze status '{status}' is not supported."),
        };
    }
}
