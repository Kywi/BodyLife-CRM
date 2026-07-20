using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class GetClientHistoryQueryHandler(
    IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
        auditEntriesQueryHandler,
    IBodyLifeQueryHandler<
        GetClientMembershipHistorySourceRowsQuery,
        GetClientMembershipHistorySourceRowsResult> membershipSourceRowsQueryHandler,
    IBodyLifeQueryHandler<
        GetClientVisitHistorySourceRowsQuery,
        GetClientVisitHistorySourceRowsResult> visitSourceRowsQueryHandler,
    IBodyLifeQueryHandler<
        GetClientPaymentHistorySourceRowsQuery,
        GetClientPaymentHistorySourceRowsResult> paymentSourceRowsQueryHandler,
    IBodyLifeQueryHandler<
        GetClientFreezeHistorySourceRowsQuery,
        GetClientFreezeHistorySourceRowsResult> freezeSourceRowsQueryHandler,
    IBodyLifeQueryHandler<
        GetClientNonWorkingDayHistorySourceRowsQuery,
        GetClientNonWorkingDayHistorySourceRowsResult>
        nonWorkingDaySourceRowsQueryHandler)
    : IBodyLifeQueryHandler<GetClientHistoryQuery, GetClientHistoryResult>
{
    private static readonly ClientHistoryEntityFilter[] AllEntityFilters =
    [
        ClientHistoryEntityFilter.Membership,
        ClientHistoryEntityFilter.MembershipOpeningState,
        ClientHistoryEntityFilter.Visit,
        ClientHistoryEntityFilter.Payment,
        ClientHistoryEntityFilter.Freeze,
        ClientHistoryEntityFilter.NonWorkingDay,
    ];

    public async Task<GetClientHistoryResult> ExecuteAsync(
        GetClientHistoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!TryNormalizeEntityFilters(query.EntityFilters, out var entityFilters))
        {
            return GetClientHistoryResult.Invalid(
                "Entity filter is invalid.",
                "entityFilters");
        }

        BuildAuditSelectors(
            entityFilters,
            out var auditEntityFilters,
            out var actionTypes);
        var auditResult = await auditEntriesQueryHandler.ExecuteAsync(
            new GetClientAuditEntriesQuery(
                query.Actor,
                query.ClientId,
                query.OccurredFromInclusive,
                query.OccurredBeforeExclusive,
                auditEntityFilters,
                actionTypes,
                query.Limit,
                query.Offset),
            cancellationToken);
        if (auditResult.Status != GetClientAuditEntriesStatus.Success)
        {
            return MapAuditFailure(auditResult);
        }

        var auditPage = auditResult.Page;
        if (auditPage is null
            || auditPage.ClientId != query.ClientId
            || auditPage.Offset != query.Offset
            || !auditPage.EntityFilters.SequenceEqual(auditEntityFilters)
            || !auditPage.ActionTypes.SequenceEqual(actionTypes)
            || auditPage.Items.Count > query.Limit
            || auditPage.Items.Select(item => item.AuditEntryId).Distinct().Count()
                != auditPage.Items.Count
            || auditPage.Items.Any(item => GetSourceGroup(item) is null))
        {
            return GetClientHistoryResult.InconsistentSource();
        }

        var auditEntriesById = auditPage.Items.ToDictionary(
            item => item.AuditEntryId);
        var mappedRows = new Dictionary<AuditEntryId, ClientHistorySourceRow>(
            auditPage.Items.Count);

        var membershipIds = SelectAuditEntryIds(
            auditPage.Items,
            ClientHistorySourceGroup.Membership);
        if (membershipIds.Length > 0)
        {
            var result = await membershipSourceRowsQueryHandler.ExecuteAsync(
                new GetClientMembershipHistorySourceRowsQuery(
                    query.Actor,
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    Limit: membershipIds.Length,
                    Offset: 0,
                    AuditEntryIds: membershipIds),
                cancellationToken);
            if (result.Status != GetClientMembershipHistorySourceRowsStatus.Success)
            {
                return MapMembershipFailure(result);
            }

            if (result.Page is null
                || !IsExactSourcePage(
                    result.Page.ClientId,
                    result.Page.Offset,
                    result.Page.HasMore,
                    result.Page.Items.Count,
                    auditPage.ClientId,
                    membershipIds.Length)
                || !TryAddRows(
                    membershipIds,
                    result.Page.Items,
                    row => row.AuditEntry,
                    MapMembershipRow,
                    auditEntriesById,
                    mappedRows))
            {
                return GetClientHistoryResult.InconsistentSource();
            }
        }

        var visitIds = SelectAuditEntryIds(
            auditPage.Items,
            ClientHistorySourceGroup.Visit);
        if (visitIds.Length > 0)
        {
            var result = await visitSourceRowsQueryHandler.ExecuteAsync(
                new GetClientVisitHistorySourceRowsQuery(
                    query.Actor,
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    Limit: visitIds.Length,
                    Offset: 0,
                    AuditEntryIds: visitIds),
                cancellationToken);
            if (result.Status != GetClientVisitHistorySourceRowsStatus.Success)
            {
                return MapVisitFailure(result);
            }

            if (result.Page is null
                || !IsExactSourcePage(
                    result.Page.ClientId,
                    result.Page.Offset,
                    result.Page.HasMore,
                    result.Page.Items.Count,
                    auditPage.ClientId,
                    visitIds.Length)
                || !TryAddRows(
                    visitIds,
                    result.Page.Items,
                    row => row.AuditEntry,
                    MapVisitRow,
                    auditEntriesById,
                    mappedRows))
            {
                return GetClientHistoryResult.InconsistentSource();
            }
        }

        var paymentIds = SelectAuditEntryIds(
            auditPage.Items,
            ClientHistorySourceGroup.Payment);
        if (paymentIds.Length > 0)
        {
            var result = await paymentSourceRowsQueryHandler.ExecuteAsync(
                new GetClientPaymentHistorySourceRowsQuery(
                    query.Actor,
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    Limit: paymentIds.Length,
                    Offset: 0,
                    AuditEntryIds: paymentIds),
                cancellationToken);
            if (result.Status != GetClientPaymentHistorySourceRowsStatus.Success)
            {
                return MapPaymentFailure(result);
            }

            if (result.Page is null
                || !IsExactSourcePage(
                    result.Page.ClientId,
                    result.Page.Offset,
                    result.Page.HasMore,
                    result.Page.Items.Count,
                    auditPage.ClientId,
                    paymentIds.Length)
                || !TryAddRows(
                    paymentIds,
                    result.Page.Items,
                    row => row.AuditEntry,
                    MapPaymentRow,
                    auditEntriesById,
                    mappedRows))
            {
                return GetClientHistoryResult.InconsistentSource();
            }
        }

        var freezeIds = SelectAuditEntryIds(
            auditPage.Items,
            ClientHistorySourceGroup.Freeze);
        if (freezeIds.Length > 0)
        {
            var result = await freezeSourceRowsQueryHandler.ExecuteAsync(
                new GetClientFreezeHistorySourceRowsQuery(
                    query.Actor,
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    Limit: freezeIds.Length,
                    Offset: 0,
                    AuditEntryIds: freezeIds),
                cancellationToken);
            if (result.Status != GetClientFreezeHistorySourceRowsStatus.Success)
            {
                return MapFreezeFailure(result);
            }

            if (result.Page is null
                || !IsExactSourcePage(
                    result.Page.ClientId,
                    result.Page.Offset,
                    result.Page.HasMore,
                    result.Page.Items.Count,
                    auditPage.ClientId,
                    freezeIds.Length)
                || !TryAddRows(
                    freezeIds,
                    result.Page.Items,
                    row => row.AuditEntry,
                    MapFreezeRow,
                    auditEntriesById,
                    mappedRows))
            {
                return GetClientHistoryResult.InconsistentSource();
            }
        }

        var nonWorkingDayIds = SelectAuditEntryIds(
            auditPage.Items,
            ClientHistorySourceGroup.NonWorkingDay);
        if (nonWorkingDayIds.Length > 0)
        {
            var result = await nonWorkingDaySourceRowsQueryHandler.ExecuteAsync(
                new GetClientNonWorkingDayHistorySourceRowsQuery(
                    query.Actor,
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    Limit: nonWorkingDayIds.Length,
                    Offset: 0,
                    AuditEntryIds: nonWorkingDayIds),
                cancellationToken);
            if (result.Status
                != GetClientNonWorkingDayHistorySourceRowsStatus.Success)
            {
                return MapNonWorkingDayFailure(result);
            }

            if (result.Page is null
                || !IsExactSourcePage(
                    result.Page.ClientId,
                    result.Page.Offset,
                    result.Page.HasMore,
                    result.Page.Items.Count,
                    auditPage.ClientId,
                    nonWorkingDayIds.Length)
                || !TryAddRows(
                    nonWorkingDayIds,
                    result.Page.Items,
                    row => row.AuditEntry,
                    MapNonWorkingDayRow,
                    auditEntriesById,
                    mappedRows))
            {
                return GetClientHistoryResult.InconsistentSource();
            }
        }

        if (mappedRows.Count != auditPage.Items.Count)
        {
            return GetClientHistoryResult.InconsistentSource();
        }

        try
        {
            var rows = auditPage.Items
                .Select(item => mappedRows[item.AuditEntryId])
                .ToArray();
            return GetClientHistoryResult.Succeeded(
                ClientHistoryPage.Create(
                    auditPage.ClientId,
                    auditPage.OccurredFromInclusive,
                    auditPage.OccurredBeforeExclusive,
                    entityFilters,
                    auditPage.Offset,
                    rows,
                    auditPage.HasMore));
        }
        catch (ArgumentException)
        {
            return GetClientHistoryResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientHistoryResult.InconsistentSource();
        }
    }

    private static bool TryNormalizeEntityFilters(
        IReadOnlyCollection<ClientHistoryEntityFilter>? requestedFilters,
        out IReadOnlyList<ClientHistoryEntityFilter> entityFilters)
    {
        var requested = requestedFilters?.ToArray() ?? [];
        if (requested.Any(filter => !Enum.IsDefined(filter)))
        {
            entityFilters = [];
            return false;
        }

        var selected = requested.Length == 0
            ? AllEntityFilters
            : AllEntityFilters.Where(requested.Contains).ToArray();
        entityFilters = Array.AsReadOnly(selected);
        return true;
    }

    private static void BuildAuditSelectors(
        IReadOnlyList<ClientHistoryEntityFilter> entityFilters,
        out IReadOnlyList<ClientAuditEntityFilter> auditEntityFilters,
        out IReadOnlyList<string> actionTypes)
    {
        var entities = new List<ClientAuditEntityFilter>(entityFilters.Count);
        var actions = new List<string>(12);
        foreach (var filter in entityFilters)
        {
            switch (filter)
            {
                case ClientHistoryEntityFilter.Membership:
                    entities.Add(ClientAuditEntityFilter.Membership);
                    actions.Add(MembershipAuditActions.Issued);
                    break;
                case ClientHistoryEntityFilter.MembershipOpeningState:
                    entities.Add(ClientAuditEntityFilter.MembershipOpeningState);
                    actions.Add(MembershipAuditActions.OpeningStateCreated);
                    break;
                case ClientHistoryEntityFilter.Visit:
                    entities.Add(ClientAuditEntityFilter.Visit);
                    actions.Add(VisitAuditActions.Marked);
                    actions.Add(VisitAuditActions.Canceled);
                    break;
                case ClientHistoryEntityFilter.Payment:
                    entities.Add(ClientAuditEntityFilter.Payment);
                    actions.Add(PaymentAuditActions.Created);
                    actions.Add(PaymentAuditActions.Corrected);
                    actions.Add(PaymentAuditActions.Canceled);
                    break;
                case ClientHistoryEntityFilter.Freeze:
                    entities.Add(ClientAuditEntityFilter.Freeze);
                    actions.Add(FreezeAuditActions.Added);
                    actions.Add(FreezeAuditActions.Canceled);
                    break;
                case ClientHistoryEntityFilter.NonWorkingDay:
                    entities.Add(ClientAuditEntityFilter.NonWorkingPeriod);
                    actions.Add(NonWorkingDayAuditActions.Added);
                    actions.Add(NonWorkingDayAuditActions.Corrected);
                    actions.Add(NonWorkingDayAuditActions.Canceled);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entityFilters));
            }
        }

        auditEntityFilters = Array.AsReadOnly(entities.ToArray());
        actionTypes = Array.AsReadOnly(actions.ToArray());
    }

    private static AuditEntryId[] SelectAuditEntryIds(
        IReadOnlyList<ClientAuditEntry> auditEntries,
        ClientHistorySourceGroup group)
    {
        return auditEntries
            .Where(entry => GetSourceGroup(entry) == group)
            .Select(entry => entry.AuditEntryId)
            .ToArray();
    }

    private static ClientHistorySourceGroup? GetSourceGroup(ClientAuditEntry entry)
    {
        return (entry.EntityType, entry.ActionType) switch
        {
            (ClientAuditEntityFilter.Membership, MembershipAuditActions.Issued)
                => ClientHistorySourceGroup.Membership,
            (ClientAuditEntityFilter.MembershipOpeningState,
                MembershipAuditActions.OpeningStateCreated)
                => ClientHistorySourceGroup.Membership,
            (ClientAuditEntityFilter.Visit, VisitAuditActions.Marked)
                => ClientHistorySourceGroup.Visit,
            (ClientAuditEntityFilter.Visit, VisitAuditActions.Canceled)
                => ClientHistorySourceGroup.Visit,
            (ClientAuditEntityFilter.Payment, PaymentAuditActions.Created)
                => ClientHistorySourceGroup.Payment,
            (ClientAuditEntityFilter.Payment, PaymentAuditActions.Corrected)
                => ClientHistorySourceGroup.Payment,
            (ClientAuditEntityFilter.Payment, PaymentAuditActions.Canceled)
                => ClientHistorySourceGroup.Payment,
            (ClientAuditEntityFilter.Freeze, FreezeAuditActions.Added)
                => ClientHistorySourceGroup.Freeze,
            (ClientAuditEntityFilter.Freeze, FreezeAuditActions.Canceled)
                => ClientHistorySourceGroup.Freeze,
            (ClientAuditEntityFilter.NonWorkingPeriod,
                NonWorkingDayAuditActions.Added)
                => ClientHistorySourceGroup.NonWorkingDay,
            (ClientAuditEntityFilter.NonWorkingPeriod,
                NonWorkingDayAuditActions.Corrected)
                => ClientHistorySourceGroup.NonWorkingDay,
            (ClientAuditEntityFilter.NonWorkingPeriod,
                NonWorkingDayAuditActions.Canceled)
                => ClientHistorySourceGroup.NonWorkingDay,
            _ => null,
        };
    }

    private static bool IsExactSourcePage(
        Guid sourceClientId,
        int sourceOffset,
        bool sourceHasMore,
        int sourceCount,
        Guid expectedClientId,
        int expectedCount)
    {
        return sourceClientId == expectedClientId
            && sourceOffset == 0
            && !sourceHasMore
            && sourceCount == expectedCount;
    }

    private static bool TryAddRows<TSourceRow>(
        IReadOnlyCollection<AuditEntryId> expectedIds,
        IEnumerable<TSourceRow> sourceRows,
        Func<TSourceRow, ClientAuditEntry> auditEntrySelector,
        Func<TSourceRow, ClientHistorySourceRow> rowMapper,
        IReadOnlyDictionary<AuditEntryId, ClientAuditEntry> auditEntriesById,
        IDictionary<AuditEntryId, ClientHistorySourceRow> mappedRows)
    {
        var expected = expectedIds.ToHashSet();
        var added = 0;
        foreach (var sourceRow in sourceRows)
        {
            var sourceAuditEntry = auditEntrySelector(sourceRow);
            if (sourceAuditEntry is null
                || !expected.Contains(sourceAuditEntry.AuditEntryId)
                || !auditEntriesById.TryGetValue(
                    sourceAuditEntry.AuditEntryId,
                    out var selectedAuditEntry)
                || sourceAuditEntry != selectedAuditEntry
                || !mappedRows.TryAdd(
                    sourceAuditEntry.AuditEntryId,
                    rowMapper(sourceRow)))
            {
                return false;
            }

            added++;
        }

        return added == expected.Count;
    }

    private static ClientHistorySourceRow MapMembershipRow(
        ClientMembershipHistorySourceRow row)
    {
        var kind = row.Kind switch
        {
            ClientMembershipHistorySourceKind.IssuedMembership
                => ClientHistorySourceKind.MembershipIssued,
            ClientMembershipHistorySourceKind.OpeningState
                => ClientHistorySourceKind.MembershipOpeningStateCreated,
            _ => throw new InvalidOperationException(
                "Unsupported Membership history source kind."),
        };
        return new ClientHistorySourceRow(
            kind,
            row.ClientId,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            row,
            VisitSourceRow: null,
            PaymentSourceRow: null,
            FreezeSourceRow: null,
            NonWorkingDaySourceRow: null,
            row.AuditEntry);
    }

    private static ClientHistorySourceRow MapVisitRow(
        ClientVisitHistorySourceRow row)
    {
        var kind = row.Kind switch
        {
            ClientVisitHistorySourceKind.MarkedVisit
                => ClientHistorySourceKind.VisitMarked,
            ClientVisitHistorySourceKind.CanceledVisit
                => ClientHistorySourceKind.VisitCanceled,
            _ => throw new InvalidOperationException(
                "Unsupported Visit history source kind."),
        };
        return new ClientHistorySourceRow(
            kind,
            row.ClientId,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            MembershipSourceRow: null,
            row,
            PaymentSourceRow: null,
            FreezeSourceRow: null,
            NonWorkingDaySourceRow: null,
            row.AuditEntry);
    }

    private static ClientHistorySourceRow MapPaymentRow(
        ClientPaymentHistorySourceRow row)
    {
        var kind = row.Kind switch
        {
            ClientPaymentHistorySourceKind.CreatedPayment
                => ClientHistorySourceKind.PaymentCreated,
            ClientPaymentHistorySourceKind.CorrectedPayment
                => ClientHistorySourceKind.PaymentCorrected,
            ClientPaymentHistorySourceKind.CanceledPayment
                => ClientHistorySourceKind.PaymentCanceled,
            _ => throw new InvalidOperationException(
                "Unsupported Payment history source kind."),
        };
        return new ClientHistorySourceRow(
            kind,
            row.ClientId,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            MembershipSourceRow: null,
            VisitSourceRow: null,
            row,
            FreezeSourceRow: null,
            NonWorkingDaySourceRow: null,
            row.AuditEntry);
    }

    private static ClientHistorySourceRow MapFreezeRow(
        ClientFreezeHistorySourceRow row)
    {
        var kind = row.Kind switch
        {
            ClientFreezeHistorySourceKind.AddedFreeze
                => ClientHistorySourceKind.FreezeAdded,
            ClientFreezeHistorySourceKind.CanceledFreeze
                => ClientHistorySourceKind.FreezeCanceled,
            _ => throw new InvalidOperationException(
                "Unsupported Freeze history source kind."),
        };
        return new ClientHistorySourceRow(
            kind,
            row.ClientId,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            MembershipSourceRow: null,
            VisitSourceRow: null,
            PaymentSourceRow: null,
            row,
            NonWorkingDaySourceRow: null,
            row.AuditEntry);
    }

    private static ClientHistorySourceRow MapNonWorkingDayRow(
        ClientNonWorkingDayHistorySourceRow row)
    {
        var kind = row.Kind switch
        {
            ClientNonWorkingDayHistorySourceKind.Added
                => ClientHistorySourceKind.NonWorkingDayAdded,
            ClientNonWorkingDayHistorySourceKind.Corrected
                => ClientHistorySourceKind.NonWorkingDayCorrected,
            ClientNonWorkingDayHistorySourceKind.Canceled
                => ClientHistorySourceKind.NonWorkingDayCanceled,
            _ => throw new InvalidOperationException(
                "Unsupported NonWorkingDay history source kind."),
        };
        return new ClientHistorySourceRow(
            kind,
            row.ClientId,
            row.OccurredAt,
            row.RecordedAt,
            row.EntryOrigin,
            MembershipSourceRow: null,
            VisitSourceRow: null,
            PaymentSourceRow: null,
            FreezeSourceRow: null,
            row,
            row.AuditEntry);
    }

    private static GetClientHistoryResult MapAuditFailure(
        GetClientAuditEntriesResult result)
    {
        return result.Status switch
        {
            GetClientAuditEntriesStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientAuditEntriesStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage ?? "Client history selectors are invalid.",
                    result.ErrorField),
            GetClientAuditEntriesStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private static GetClientHistoryResult MapMembershipFailure(
        GetClientMembershipHistorySourceRowsResult result)
    {
        return result.Status switch
        {
            GetClientMembershipHistorySourceRowsStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientMembershipHistorySourceRowsStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage ?? "Membership history request is invalid.",
                    result.ErrorField),
            GetClientMembershipHistorySourceRowsStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private static GetClientHistoryResult MapVisitFailure(
        GetClientVisitHistorySourceRowsResult result)
    {
        return result.Status switch
        {
            GetClientVisitHistorySourceRowsStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientVisitHistorySourceRowsStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage ?? "Visit history request is invalid.",
                    result.ErrorField),
            GetClientVisitHistorySourceRowsStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private static GetClientHistoryResult MapPaymentFailure(
        GetClientPaymentHistorySourceRowsResult result)
    {
        return result.Status switch
        {
            GetClientPaymentHistorySourceRowsStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientPaymentHistorySourceRowsStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage ?? "Payment history request is invalid.",
                    result.ErrorField),
            GetClientPaymentHistorySourceRowsStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private static GetClientHistoryResult MapFreezeFailure(
        GetClientFreezeHistorySourceRowsResult result)
    {
        return result.Status switch
        {
            GetClientFreezeHistorySourceRowsStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientFreezeHistorySourceRowsStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage ?? "Freeze history request is invalid.",
                    result.ErrorField),
            GetClientFreezeHistorySourceRowsStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private static GetClientHistoryResult MapNonWorkingDayFailure(
        GetClientNonWorkingDayHistorySourceRowsResult result)
    {
        return result.Status switch
        {
            GetClientNonWorkingDayHistorySourceRowsStatus.PermissionDenied
                => GetClientHistoryResult.Denied(),
            GetClientNonWorkingDayHistorySourceRowsStatus.ValidationFailed
                => GetClientHistoryResult.Invalid(
                    result.ErrorMessage
                        ?? "NonWorkingDay history request is invalid.",
                    result.ErrorField),
            GetClientNonWorkingDayHistorySourceRowsStatus.NotFound
                => GetClientHistoryResult.MissingClient(),
            _ => GetClientHistoryResult.InconsistentSource(),
        };
    }

    private enum ClientHistorySourceGroup
    {
        Membership = 1,
        Visit,
        Payment,
        Freeze,
        NonWorkingDay,
    }
}
