using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientMembershipReportStatesQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetClientMembershipReportStatesQuery,
        GetClientMembershipReportStatesResult>
{
    public async Task<GetClientMembershipReportStatesResult> ExecuteAsync(
        GetClientMembershipReportStatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientMembershipReportStatesResult.Denied();
        }

        var validationFailure = Validate(query, out var clientIds);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var requestedClientIds = clientIds!;
        var existingClientIds = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .Where(client => requestedClientIds.Contains(client.Id))
            .Select(client => client.Id)
            .ToArrayAsync(cancellationToken);
        if (existingClientIds.Length != requestedClientIds.Length)
        {
            return GetClientMembershipReportStatesResult.InconsistentSource();
        }

        var membershipRows = await (
            from membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            where requestedClientIds.Contains(membership.ClientId)
            join cache in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on membership.Id equals cache.MembershipId into membershipCaches
            from cache in membershipCaches.DefaultIfEmpty()
            orderby membership.ClientId,
                membership.StartDate descending,
                membership.IssuedAt descending,
                membership.Id
            select new ClientMembershipReportStateStorageRow(membership, cache))
            .ToArrayAsync(cancellationToken);

        foreach (var row in membershipRows)
        {
            if (row.Cache is null
                || row.Cache.RecalculationVersion
                    != MembershipStateCacheRebuilder.CurrentRecalculationVersion
                || !MembershipQuerySupport.TryMapLifecycleStatus(
                    row.Membership.Status,
                    out _))
            {
                return GetClientMembershipReportStatesResult.RecalculationFailed();
            }
        }

        var membershipIds = membershipRows
            .Select(row => row.Membership.Id)
            .ToArray();
        var extensionRows = membershipIds.Length == 0
            ? []
            : await dbContext.Set<MembershipExtensionDayRecord>()
                .AsNoTracking()
                .Where(extensionDay => membershipIds.Contains(extensionDay.MembershipId))
                .ToArrayAsync(cancellationToken);
        var extensionRowsByMembershipId = extensionRows.ToLookup(
            extensionDay => extensionDay.MembershipId);
        var membershipRowsByClientId = membershipRows.ToLookup(
            row => row.Membership.ClientId);
        var reportStates = new List<ClientMembershipReportState>(
            requestedClientIds.Length);

        foreach (var clientId in requestedClientIds)
        {
            var timeline = new List<ClientMembershipStateTimelineItem>();
            foreach (var row in membershipRowsByClientId[clientId])
            {
                if (!MembershipQuerySupport.TryMapLifecycleStatus(
                        row.Membership.Status,
                        out var lifecycleStatus)
                    || !MembershipStateReadModelFactory.TryCreate(
                        row.Membership,
                        row.Cache!,
                        query.AsOfDate,
                        extensionRowsByMembershipId[row.Membership.Id],
                        out var state))
                {
                    return GetClientMembershipReportStatesResult.RecalculationFailed();
                }

                timeline.Add(new ClientMembershipStateTimelineItem(
                    state,
                    lifecycleStatus,
                    row.Membership.IssuedAt));
            }

            try
            {
                reportStates.Add(new ClientMembershipReportState(
                    clientId,
                    ClientMembershipStatesPolicy.Create(
                        clientId,
                        query.AsOfDate,
                        timeline)));
            }
            catch (ArgumentException)
            {
                return GetClientMembershipReportStatesResult.RecalculationFailed();
            }
        }

        try
        {
            return GetClientMembershipReportStatesResult.Succeeded(
                new ClientMembershipReportStates(
                    query.AsOfDate,
                    reportStates));
        }
        catch (ArgumentException)
        {
            return GetClientMembershipReportStatesResult.InconsistentSource();
        }
    }

    private static GetClientMembershipReportStatesResult? Validate(
        GetClientMembershipReportStatesQuery query,
        out Guid[]? clientIds)
    {
        clientIds = null;
        if (query.AsOfDate == default)
        {
            return GetClientMembershipReportStatesResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        if (query.ClientIds is null
            || query.ClientIds.Count is < 1
                or > GetClientMembershipReportStatesQuery.MaxClientCount)
        {
            return GetClientMembershipReportStatesResult.Invalid(
                $"Client ids must contain between 1 and {GetClientMembershipReportStatesQuery.MaxClientCount} items.",
                "clientIds");
        }

        clientIds = query.ClientIds.ToArray();
        if (clientIds.Any(clientId => clientId == Guid.Empty)
            || clientIds.Distinct().Count() != clientIds.Length)
        {
            clientIds = null;
            return GetClientMembershipReportStatesResult.Invalid(
                "Client ids must be non-empty and unique.",
                "clientIds");
        }

        return null;
    }

    private sealed record ClientMembershipReportStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord? Cache);
}
