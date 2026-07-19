using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class ListInactiveClientsQueryHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<
        GetClientMembershipReportStatesQuery,
        GetClientMembershipReportStatesResult> getMembershipStates,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<ListInactiveClientsQuery, ListInactiveClientsResult>
{
    public async Task<ListInactiveClientsResult> ExecuteAsync(
        ListInactiveClientsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await VisitQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return ListInactiveClientsResult.Denied();
        }

        var validationFailure = Validate(query);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var asOfEnd = UtcStartOfDay(query.AsOfDate.AddDays(1));
        var inactivityCutoffExclusive = UtcStartOfDay(
            query.AsOfDate.AddDays(1 - query.ThresholdDays));
        var activeVisitsAsOf = dbContext.Set<VisitRecord>()
            .AsNoTracking()
            .Where(visit => visit.Status == VisitQuerySupport.ActiveStatus
                && visit.OccurredAt < asOfEnd);
        var currentCards = dbContext.Set<ClientCardAssignmentRecord>()
            .AsNoTracking()
            .Where(card => card.IsCurrent);
        var candidateRows = await (
            from client in dbContext.Set<ClientRecord>().AsNoTracking()
            join card in currentCards
                on client.Id equals card.ClientId into currentCardRows
            from currentCard in currentCardRows.DefaultIfEmpty()
            let lastCountedVisitAt = activeVisitsAsOf
                .Where(visit => visit.ClientId == client.Id)
                .Max(visit => (DateTimeOffset?)visit.OccurredAt)
            where (lastCountedVisitAt != null
                    && lastCountedVisitAt < inactivityCutoffExclusive)
                || (lastCountedVisitAt == null
                    && query.IncludeClientsWithNoVisits)
            orderby lastCountedVisitAt == null ? 1 : 0,
                lastCountedVisitAt,
                client.NormalizedFullName,
                client.Id
            select new InactiveClientStorageRow(
                client.Id,
                client.Surname,
                client.Name,
                client.Patronymic,
                client.PhoneRaw,
                currentCard == null ? null : currentCard.CardNumberRaw,
                client.OperationalStatus,
                lastCountedVisitAt))
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = candidateRows.Length > query.Limit;
        var visibleRows = candidateRows.Take(query.Limit).ToArray();
        if (visibleRows.Length == 0)
        {
            return TryBuildResult(query, sourceRows: [], hasMore: false);
        }

        var clientIds = visibleRows.Select(row => row.ClientId).ToArray();
        var latestVisits = await activeVisitsAsOf
            .Where(visit => clientIds.Contains(visit.ClientId))
            .GroupBy(visit => visit.ClientId)
            .Select(group => group
                .OrderByDescending(visit => visit.OccurredAt)
                .ThenByDescending(visit => visit.RecordedAt)
                .ThenByDescending(visit => visit.Id)
                .Select(visit => new LatestVisitStorageRow(
                    visit.Id,
                    visit.ClientId,
                    visit.OccurredAt,
                    visit.VisitKind))
                .First())
            .ToArrayAsync(cancellationToken);
        var latestVisitByClientId = latestVisits.ToDictionary(
            visit => visit.ClientId);

        var membershipResult = await getMembershipStates.ExecuteAsync(
            new GetClientMembershipReportStatesQuery(
                query.Actor,
                query.AsOfDate,
                clientIds),
            cancellationToken);
        if (membershipResult.Status
            != GetClientMembershipReportStatesStatus.Success)
        {
            return MapMembershipFailure(membershipResult);
        }

        if (membershipResult.States is null
            || membershipResult.States.AsOfDate != query.AsOfDate
            || membershipResult.States.Clients.Count != clientIds.Length)
        {
            return ListInactiveClientsResult.InconsistentSource();
        }

        var membershipStatesByClientId = membershipResult.States.Clients
            .ToDictionary(client => client.ClientId, client => client.States);
        if (clientIds.Any(clientId => !membershipStatesByClientId.ContainsKey(clientId)))
        {
            return ListInactiveClientsResult.InconsistentSource();
        }

        var sourceRows = new List<InactiveClientSourceRow>(visibleRows.Length);
        foreach (var row in visibleRows)
        {
            InactiveClientLastVisit? lastVisit = null;
            if (row.LastCountedVisitAt is not null)
            {
                if (!latestVisitByClientId.TryGetValue(row.ClientId, out var storedVisit)
                    || storedVisit.OccurredAt != row.LastCountedVisitAt
                    || !VisitQuerySupport.TryMapVisitKind(
                        storedVisit.VisitKind,
                        out var visitKind))
                {
                    return ListInactiveClientsResult.InconsistentSource();
                }

                lastVisit = new InactiveClientLastVisit(
                    storedVisit.VisitId,
                    storedVisit.OccurredAt,
                    visitKind);
            }
            else if (latestVisitByClientId.ContainsKey(row.ClientId))
            {
                return ListInactiveClientsResult.InconsistentSource();
            }

            try
            {
                sourceRows.Add(new InactiveClientSourceRow(
                    row.ClientId,
                    ClientQuerySupport.BuildDisplayName(
                        row.ClientSurname,
                        row.ClientName,
                        row.ClientPatronymic),
                    row.ClientPhone,
                    row.CurrentCardNumber,
                    ClientQuerySupport.MapOperationalStatus(row.OperationalStatus),
                    lastVisit,
                    membershipStatesByClientId[row.ClientId]));
            }
            catch (ArgumentException)
            {
                return ListInactiveClientsResult.InconsistentSource();
            }
            catch (InvalidOperationException)
            {
                return ListInactiveClientsResult.InconsistentSource();
            }
        }

        return TryBuildResult(query, sourceRows, hasMore);
    }

    private static ListInactiveClientsResult? Validate(
        ListInactiveClientsQuery query)
    {
        if (query.AsOfDate == default
            || query.AsOfDate == DateOnly.MaxValue)
        {
            return ListInactiveClientsResult.Invalid(
                "As-of date is outside the supported UTC report range.",
                "asOfDate");
        }

        if (!ListInactiveClientsQuery.IsSupportedThreshold(query.ThresholdDays))
        {
            return ListInactiveClientsResult.Invalid(
                "Threshold must be 14, 30 or 60 days.",
                "thresholdDays");
        }

        if (query.AsOfDate.DayNumber < query.ThresholdDays - 1)
        {
            return ListInactiveClientsResult.Invalid(
                "As-of date cannot represent the selected inactivity threshold.",
                "asOfDate");
        }

        if (query.Limit is < 1 or > ListInactiveClientsQuery.MaxLimit)
        {
            return ListInactiveClientsResult.Invalid(
                $"Limit must be between 1 and {ListInactiveClientsQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > ListInactiveClientsQuery.MaxOffset)
        {
            return ListInactiveClientsResult.Invalid(
                $"Offset must be between 0 and {ListInactiveClientsQuery.MaxOffset}.",
                "offset");
        }

        return null;
    }

    private static ListInactiveClientsResult MapMembershipFailure(
        GetClientMembershipReportStatesResult result)
    {
        return result.Status switch
        {
            GetClientMembershipReportStatesStatus.PermissionDenied
                => ListInactiveClientsResult.Denied(),
            GetClientMembershipReportStatesStatus.ValidationFailed
                => ListInactiveClientsResult.Invalid(
                    result.ErrorMessage
                        ?? "Client Membership report state request is invalid.",
                    result.ErrorField),
            GetClientMembershipReportStatesStatus.RecalculationFailed
                => ListInactiveClientsResult.RecalculationFailed(),
            _ => ListInactiveClientsResult.InconsistentSource(),
        };
    }

    private static ListInactiveClientsResult TryBuildResult(
        ListInactiveClientsQuery query,
        IEnumerable<InactiveClientSourceRow> sourceRows,
        bool hasMore)
    {
        try
        {
            return InactiveClientsPage.TryCreate(
                query,
                sourceRows,
                hasMore,
                out var page)
                && page is not null
                    ? ListInactiveClientsResult.Succeeded(page)
                    : ListInactiveClientsResult.InconsistentSource();
        }
        catch (ArgumentException)
        {
            return ListInactiveClientsResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return ListInactiveClientsResult.InconsistentSource();
        }
    }

    private static DateTimeOffset UtcStartOfDay(DateOnly date)
    {
        return new DateTimeOffset(
            date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    private sealed record InactiveClientStorageRow(
        Guid ClientId,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        string? ClientPhone,
        string? CurrentCardNumber,
        string OperationalStatus,
        DateTimeOffset? LastCountedVisitAt);

    private sealed record LatestVisitStorageRow(
        Guid VisitId,
        Guid ClientId,
        DateTimeOffset OccurredAt,
        string VisitKind);
}
