using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetClientMembershipStatesQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetClientMembershipStatesQuery,
        GetClientMembershipStatesResult>
{
    public async Task<GetClientMembershipStatesResult> ExecuteAsync(
        GetClientMembershipStatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientMembershipStatesResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return GetClientMembershipStatesResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.AsOfDate == default)
        {
            return GetClientMembershipStatesResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return GetClientMembershipStatesResult.MissingClient();
        }

        var membershipRows = await (
            from membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            where membership.ClientId == query.ClientId
            join cache in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on membership.Id equals cache.MembershipId into membershipCaches
            from cache in membershipCaches.DefaultIfEmpty()
            orderby membership.StartDate descending,
                membership.IssuedAt descending,
                membership.Id
            select new ClientMembershipStateStorageRow(membership, cache))
            .ToArrayAsync(cancellationToken);

        if (membershipRows.Length == 0)
        {
            return GetClientMembershipStatesResult.Succeeded(
                ClientMembershipStatesPolicy.Create(query.ClientId, query.AsOfDate, []),
                MembershipQuerySupport.BuildIssueActionPermissions());
        }

        foreach (var row in membershipRows)
        {
            if (row.Cache is null
                || row.Cache.RecalculationVersion
                    != MembershipStateCacheRebuilder.CurrentRecalculationVersion
                || !MembershipQuerySupport.TryMapLifecycleStatus(
                    row.Membership.Status,
                    out _))
            {
                return GetClientMembershipStatesResult.RecalculationFailed();
            }
        }

        var membershipIds = membershipRows
            .Select(row => row.Membership.Id)
            .ToArray();
        var extensionRows = await dbContext.Set<MembershipExtensionDayRecord>()
            .AsNoTracking()
            .Where(extensionDay => membershipIds.Contains(extensionDay.MembershipId))
            .ToArrayAsync(cancellationToken);
        var extensionRowsByMembershipId = extensionRows.ToLookup(
            extensionDay => extensionDay.MembershipId);
        var timeline = new List<ClientMembershipStateTimelineItem>(membershipRows.Length);

        foreach (var row in membershipRows)
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
                return GetClientMembershipStatesResult.RecalculationFailed();
            }

            timeline.Add(new ClientMembershipStateTimelineItem(
                state,
                lifecycleStatus,
                row.Membership.IssuedAt));
        }

        ClientMembershipStatesReadModel collection;

        try
        {
            collection = ClientMembershipStatesPolicy.Create(
                query.ClientId,
                query.AsOfDate,
                timeline);
        }
        catch (ArgumentException)
        {
            return GetClientMembershipStatesResult.RecalculationFailed();
        }

        return GetClientMembershipStatesResult.Succeeded(
            collection,
            MembershipQuerySupport.BuildIssueActionPermissions());
    }

    private sealed record ClientMembershipStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord? Cache);
}
