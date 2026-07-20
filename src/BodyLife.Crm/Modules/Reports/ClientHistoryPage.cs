using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Modules.Reports;

public sealed class ClientHistoryPage
{
    private ClientHistoryPage(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IReadOnlyList<ClientHistoryEntityFilter> entityFilters,
        int offset,
        IReadOnlyList<ClientHistorySourceRow> items,
        bool hasMore)
    {
        ClientId = clientId;
        OccurredFromInclusive = occurredFromInclusive;
        OccurredBeforeExclusive = occurredBeforeExclusive;
        EntityFilters = entityFilters;
        Offset = offset;
        Items = items;
        HasMore = hasMore;
        NextOffset = hasMore ? offset + items.Count : null;
    }

    public Guid ClientId { get; }

    public DateTimeOffset? OccurredFromInclusive { get; }

    public DateTimeOffset? OccurredBeforeExclusive { get; }

    public IReadOnlyList<ClientHistoryEntityFilter> EntityFilters { get; }

    public int Offset { get; }

    public IReadOnlyList<ClientHistorySourceRow> Items { get; }

    public bool HasMore { get; }

    public int? NextOffset { get; }

    public static ClientHistoryPage Create(
        Guid clientId,
        DateTimeOffset? occurredFromInclusive,
        DateTimeOffset? occurredBeforeExclusive,
        IEnumerable<ClientHistoryEntityFilter> entityFilters,
        int offset,
        IEnumerable<ClientHistorySourceRow> items,
        bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(entityFilters);
        ArgumentNullException.ThrowIfNull(items);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var filterSnapshot = entityFilters.Distinct().ToArray();
        if (filterSnapshot.Any(filter => !Enum.IsDefined(filter)))
        {
            throw new ArgumentException("Entity filter is invalid.", nameof(entityFilters));
        }

        var itemSnapshot = items.ToArray();
        if (itemSnapshot.Any(item => !IsCanonicalRow(item, clientId)))
        {
            throw new ArgumentException(
                "Every Client history row must carry one matching canonical source and audit entry.",
                nameof(items));
        }

        return new ClientHistoryPage(
            clientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            Array.AsReadOnly(filterSnapshot),
            offset,
            Array.AsReadOnly(itemSnapshot),
            hasMore);
    }

    private static bool IsCanonicalRow(ClientHistorySourceRow? row, Guid clientId)
    {
        if (row is null
            || row.ClientId != clientId
            || row.AuditEntry is null
            || row.OccurredAt != row.AuditEntry.OccurredAt
            || row.RecordedAt != row.AuditEntry.RecordedAt
            || row.EntryOrigin != row.AuditEntry.EntryOrigin)
        {
            return false;
        }

        var sourceCount = new object?[]
        {
            row.MembershipSourceRow,
            row.VisitSourceRow,
            row.PaymentSourceRow,
            row.FreezeSourceRow,
            row.NonWorkingDaySourceRow,
        }.Count(source => source is not null);
        if (sourceCount != 1)
        {
            return false;
        }

        return row.Kind switch
        {
            ClientHistorySourceKind.MembershipIssued
                => MatchesSource(
                    row,
                    row.MembershipSourceRow,
                    ClientMembershipHistorySourceKind.IssuedMembership),
            ClientHistorySourceKind.MembershipOpeningStateCreated
                => MatchesSource(
                    row,
                    row.MembershipSourceRow,
                    ClientMembershipHistorySourceKind.OpeningState),
            ClientHistorySourceKind.VisitMarked
                => MatchesSource(
                    row,
                    row.VisitSourceRow,
                    ClientVisitHistorySourceKind.MarkedVisit),
            ClientHistorySourceKind.VisitCanceled
                => MatchesSource(
                    row,
                    row.VisitSourceRow,
                    ClientVisitHistorySourceKind.CanceledVisit),
            ClientHistorySourceKind.PaymentCreated
                => MatchesSource(
                    row,
                    row.PaymentSourceRow,
                    ClientPaymentHistorySourceKind.CreatedPayment),
            ClientHistorySourceKind.PaymentCorrected
                => MatchesSource(
                    row,
                    row.PaymentSourceRow,
                    ClientPaymentHistorySourceKind.CorrectedPayment),
            ClientHistorySourceKind.PaymentCanceled
                => MatchesSource(
                    row,
                    row.PaymentSourceRow,
                    ClientPaymentHistorySourceKind.CanceledPayment),
            ClientHistorySourceKind.FreezeAdded
                => MatchesSource(
                    row,
                    row.FreezeSourceRow,
                    ClientFreezeHistorySourceKind.AddedFreeze),
            ClientHistorySourceKind.FreezeCanceled
                => MatchesSource(
                    row,
                    row.FreezeSourceRow,
                    ClientFreezeHistorySourceKind.CanceledFreeze),
            ClientHistorySourceKind.NonWorkingDayAdded
                => MatchesSource(
                    row,
                    row.NonWorkingDaySourceRow,
                    ClientNonWorkingDayHistorySourceKind.Added),
            ClientHistorySourceKind.NonWorkingDayCorrected
                => MatchesSource(
                    row,
                    row.NonWorkingDaySourceRow,
                    ClientNonWorkingDayHistorySourceKind.Corrected),
            ClientHistorySourceKind.NonWorkingDayCanceled
                => MatchesSource(
                    row,
                    row.NonWorkingDaySourceRow,
                    ClientNonWorkingDayHistorySourceKind.Canceled),
            _ => false,
        };
    }

    private static bool MatchesSource(
        ClientHistorySourceRow row,
        ClientMembershipHistorySourceRow? source,
        ClientMembershipHistorySourceKind expectedKind)
    {
        return source is not null
            && source.Kind == expectedKind
            && MatchesEnvelope(
                row,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.EntryOrigin,
                source.AuditEntry);
    }

    private static bool MatchesSource(
        ClientHistorySourceRow row,
        ClientVisitHistorySourceRow? source,
        ClientVisitHistorySourceKind expectedKind)
    {
        return source is not null
            && source.Kind == expectedKind
            && MatchesEnvelope(
                row,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.EntryOrigin,
                source.AuditEntry);
    }

    private static bool MatchesSource(
        ClientHistorySourceRow row,
        ClientPaymentHistorySourceRow? source,
        ClientPaymentHistorySourceKind expectedKind)
    {
        return source is not null
            && source.Kind == expectedKind
            && MatchesEnvelope(
                row,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.EntryOrigin,
                source.AuditEntry);
    }

    private static bool MatchesSource(
        ClientHistorySourceRow row,
        ClientFreezeHistorySourceRow? source,
        ClientFreezeHistorySourceKind expectedKind)
    {
        return source is not null
            && source.Kind == expectedKind
            && MatchesEnvelope(
                row,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.EntryOrigin,
                source.AuditEntry);
    }

    private static bool MatchesSource(
        ClientHistorySourceRow row,
        ClientNonWorkingDayHistorySourceRow? source,
        ClientNonWorkingDayHistorySourceKind expectedKind)
    {
        return source is not null
            && source.Kind == expectedKind
            && MatchesEnvelope(
                row,
                source.ClientId,
                source.OccurredAt,
                source.RecordedAt,
                source.EntryOrigin,
                source.AuditEntry);
    }

    private static bool MatchesEnvelope(
        ClientHistorySourceRow row,
        Guid sourceClientId,
        DateTimeOffset sourceOccurredAt,
        DateTimeOffset sourceRecordedAt,
        EntryOrigin sourceEntryOrigin,
        ClientAuditEntry sourceAuditEntry)
    {
        return sourceClientId == row.ClientId
            && sourceOccurredAt == row.OccurredAt
            && sourceRecordedAt == row.RecordedAt
            && sourceEntryOrigin == row.EntryOrigin
            && sourceAuditEntry == row.AuditEntry;
    }
}
