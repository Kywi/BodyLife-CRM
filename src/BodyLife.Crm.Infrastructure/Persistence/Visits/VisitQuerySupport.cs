using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal static class VisitQuerySupport
{
    internal const string ActiveStatus = "active";
    internal const string CanceledStatus = "canceled";
    internal const string CountedConsumptionType = "counted";
    internal const string VisitSourceFactType = "visit";

    internal static Task<bool> IsActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return actor is not null && VisitCommandSupport.IsAllowedActorShape(actor)
            ? VisitCommandSupport.IsCanonicalActorAuthorizedAsync(
                dbContext,
                actor,
                now,
                cancellationToken)
            : Task.FromResult(false);
    }

    internal static QueryPermissionSet BuildCancellationPermissions(
        ActorContext actor,
        ClientVisitRowStatus visitStatus,
        VisitDayReconciliationStatus dayStatus)
    {
        if (visitStatus == ClientVisitRowStatus.Canceled)
        {
            return QueryPermissionSet.Empty;
        }

        return dayStatus switch
        {
            VisitDayReconciliationStatus.Open => new QueryPermissionSet(
            [
                QueryPermissionResult.Allowed(
                    VisitActionKeys.Cancel,
                    VisitActionKeys.AdminOrOwnerPolicy),
            ]),
            VisitDayReconciliationStatus.Reconciled when actor.Role == ActorRole.Owner
                => new QueryPermissionSet(
                [
                    QueryPermissionResult.Allowed(
                        VisitActionKeys.Cancel,
                        VisitActionKeys.OwnerPolicy),
                ]),
            VisitDayReconciliationStatus.Reconciled => new QueryPermissionSet(
            [
                QueryPermissionResult.Denied(
                    VisitActionKeys.Cancel,
                    VisitActionKeys.OwnerPolicy,
                    "day_closed_requires_owner",
                    "Only the Owner can cancel a Visit from a reconciled day."),
            ]),
            _ => throw new ArgumentOutOfRangeException(
                nameof(dayStatus),
                dayStatus,
                null),
        };
    }

    internal static bool TryMapVisitKind(string value, out VisitKind visitKind)
    {
        visitKind = value switch
        {
            "membership" => VisitKind.Membership,
            "one_off" => VisitKind.OneOff,
            "trial" => VisitKind.Trial,
            _ => default,
        };

        return visitKind != default;
    }

    internal static bool TryMapEntryOrigin(string value, out EntryOrigin entryOrigin)
    {
        entryOrigin = value switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => default,
        };

        return Enum.IsDefined(entryOrigin);
    }

    internal static bool TryMapVisitStatus(
        string value,
        bool hasCancellation,
        out ClientVisitRowStatus status)
    {
        status = (value, hasCancellation) switch
        {
            (ActiveStatus, false) => ClientVisitRowStatus.Active,
            (CanceledStatus, true) => ClientVisitRowStatus.Canceled,
            _ => default,
        };

        return status != default;
    }

    internal static bool TryMapConsumptionStatus(
        string value,
        out ClientVisitConsumptionStatus status)
    {
        status = value switch
        {
            ActiveStatus => ClientVisitConsumptionStatus.Active,
            CanceledStatus => ClientVisitConsumptionStatus.Canceled,
            _ => default,
        };

        return status != default;
    }

    internal static bool TryMapSourceRow(
        CanonicalVisitSourceRow source,
        CanonicalVisitConsumptionSourceRow? consumptionSource,
        CanonicalVisitCancellationSourceRow? cancellationSource,
        out CanonicalVisitProjection? projection)
    {
        projection = null;
        if (!TryMapVisitKind(source.VisitKind, out var visitKind)
            || !TryMapEntryOrigin(source.EntryOrigin, out var entryOrigin)
            || !TryMapVisitStatus(
                source.Status,
                cancellationSource is not null,
                out var status))
        {
            return false;
        }

        ClientVisitConsumption? consumption = null;
        if (visitKind == VisitKind.Membership)
        {
            if (consumptionSource is null
                || consumptionSource.ClientId != source.ClientId
                || consumptionSource.MembershipClientId != source.ClientId
                || consumptionSource.VisitKind != source.VisitKind
                || consumptionSource.ConsumptionType != CountedConsumptionType
                || consumptionSource.SourceFactType != VisitSourceFactType
                || consumptionSource.SourceFactId != source.VisitId
                || string.IsNullOrWhiteSpace(
                    consumptionSource.MembershipTypeNameSnapshot)
                || !TryMapConsumptionStatus(
                    consumptionSource.Status,
                    out var consumptionStatus)
                || !StatusesAgree(status, consumptionStatus))
            {
                return false;
            }

            consumption = new ClientVisitConsumption(
                consumptionSource.ConsumptionId,
                consumptionSource.MembershipId,
                consumptionSource.MembershipTypeNameSnapshot,
                consumptionStatus);
        }
        else if (consumptionSource is not null)
        {
            return false;
        }

        ClientVisitCancellation? cancellation = null;
        if (cancellationSource is not null)
        {
            if (string.IsNullOrWhiteSpace(cancellationSource.Reason)
                || !TryMapEntryOrigin(
                    cancellationSource.EntryOrigin,
                    out var cancellationOrigin))
            {
                return false;
            }

            cancellation = new ClientVisitCancellation(
                cancellationSource.CancellationId,
                cancellationSource.Reason,
                cancellationSource.OccurredAt,
                cancellationSource.RecordedAt,
                cancellationSource.RecordedByAccountId,
                cancellationSource.SessionId,
                cancellationOrigin,
                cancellationSource.EntryBatchId);
        }

        projection = new CanonicalVisitProjection(
            visitKind,
            entryOrigin,
            status,
            consumption,
            cancellation);
        return true;
    }

    private static bool StatusesAgree(
        ClientVisitRowStatus visitStatus,
        ClientVisitConsumptionStatus consumptionStatus)
    {
        return (visitStatus, consumptionStatus) switch
        {
            (ClientVisitRowStatus.Active, ClientVisitConsumptionStatus.Active) => true,
            (ClientVisitRowStatus.Canceled, ClientVisitConsumptionStatus.Canceled) => true,
            _ => false,
        };
    }

    internal sealed record CanonicalVisitSourceRow(
        Guid VisitId,
        Guid ClientId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string VisitKind,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    internal sealed record CanonicalVisitConsumptionSourceRow(
        Guid ConsumptionId,
        Guid VisitId,
        Guid ClientId,
        string VisitKind,
        Guid MembershipId,
        Guid MembershipClientId,
        string MembershipTypeNameSnapshot,
        string ConsumptionType,
        string SourceFactType,
        Guid SourceFactId,
        string Status);

    internal sealed record CanonicalVisitCancellationSourceRow(
        Guid CancellationId,
        Guid VisitId,
        string Reason,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId);

    internal sealed record CanonicalVisitProjection(
        VisitKind VisitKind,
        EntryOrigin EntryOrigin,
        ClientVisitRowStatus Status,
        ClientVisitConsumption? Consumption,
        ClientVisitCancellation? Cancellation);
}
