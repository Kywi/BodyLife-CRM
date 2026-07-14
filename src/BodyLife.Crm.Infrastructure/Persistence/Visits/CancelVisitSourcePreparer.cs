using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Visits;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class CancelVisitSourcePreparer
{
    private readonly BodyLifeDbContext dbContext;

    public CancelVisitSourcePreparer(BodyLifeDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        this.dbContext = dbContext;
    }

    public async Task<CancelVisitSourcePreparationResult> PrepareAsync(
        Guid visitId,
        CancellationToken cancellationToken = default)
    {
        if (visitId == Guid.Empty)
        {
            throw new ArgumentException("Visit id is required.", nameof(visitId));
        }

        if (dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "CancelVisit source preparation requires a caller-owned database "
                + "transaction so the Visit and active consumption remain locked.");
        }

        var visits = await dbContext.Set<VisitRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    client_id,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    visit_kind,
                    entry_origin,
                    entry_batch_id,
                    comment,
                    status
                from bodylife.visits
                where id = {visitId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        var visit = visits.SingleOrDefault();
        if (visit is null)
        {
            return CancelVisitSourcePreparationResult.NotFound(visitId);
        }

        var activeConsumptions = await dbContext.Set<VisitConsumptionRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    visit_id,
                    client_id,
                    visit_kind,
                    membership_id,
                    consumption_type,
                    source_fact_type,
                    source_fact_id,
                    recorded_at,
                    recorded_by_account_id,
                    recorded_session_id,
                    status
                from bodylife.visit_consumptions
                where visit_id = {visitId}
                    and status = 'active'
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var cancellations = await dbContext.Set<VisitCancellationRecord>()
            .FromSqlInterpolated(
                $"""
                select
                    id,
                    visit_id,
                    reason,
                    occurred_at,
                    recorded_at,
                    recorded_by_account_id,
                    session_id,
                    entry_origin,
                    entry_batch_id
                from bodylife.visit_cancellations
                where visit_id = {visitId}
                order by id
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);

        var activeConsumption = activeConsumptions.FirstOrDefault();
        var existingCancellation = cancellations.FirstOrDefault();
        var source = new VisitCancellationSource(
            visit.Id,
            visit.ClientId,
            visit.OccurredAt,
            visit.RecordedAt,
            visit.RecordedByAccountId,
            visit.SessionId,
            MapVisitKind(visit.VisitKind),
            MapEntryOrigin(visit.EntryOrigin),
            visit.EntryBatchId,
            visit.Comment,
            MapVisitStatus(visit.Status),
            activeConsumption?.Id,
            activeConsumption?.MembershipId,
            existingCancellation?.Id);

        if (source.Status == VisitCancellationSourceStatus.Canceled
            || existingCancellation is not null)
        {
            return CancelVisitSourcePreparationResult.AlreadyCanceled(source);
        }

        if (activeConsumptions.Length > 1
            || cancellations.Length > 1
            || !HasConsistentActiveConsumption(source, activeConsumption))
        {
            return CancelVisitSourcePreparationResult.InconsistentSource(source);
        }

        return CancelVisitSourcePreparationResult.Prepared(source);
    }

    private static bool HasConsistentActiveConsumption(
        VisitCancellationSource source,
        VisitConsumptionRecord? consumption)
    {
        if (source.VisitKind != VisitKind.Membership)
        {
            return consumption is null;
        }

        return consumption is not null
            && consumption.VisitId == source.VisitId
            && consumption.ClientId == source.ClientId
            && consumption.VisitKind == "membership"
            && consumption.ConsumptionType == "counted"
            && consumption.SourceFactType == "visit"
            && consumption.SourceFactId == source.VisitId
            && consumption.Status == "active"
            && consumption.MembershipId != Guid.Empty;
    }

    private static VisitKind MapVisitKind(string visitKind)
    {
        return visitKind switch
        {
            "membership" => VisitKind.Membership,
            "one_off" => VisitKind.OneOff,
            "trial" => VisitKind.Trial,
            _ => throw new InvalidOperationException(
                $"Visit kind '{visitKind}' is not supported."),
        };
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
                $"Visit entry origin '{entryOrigin}' is not supported."),
        };
    }

    private static VisitCancellationSourceStatus MapVisitStatus(string status)
    {
        return status switch
        {
            "active" => VisitCancellationSourceStatus.Active,
            "canceled" => VisitCancellationSourceStatus.Canceled,
            _ => throw new InvalidOperationException(
                $"Visit status '{status}' is not supported."),
        };
    }
}
