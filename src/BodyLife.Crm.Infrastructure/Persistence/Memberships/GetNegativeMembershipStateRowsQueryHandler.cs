using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetNegativeMembershipStateRowsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetNegativeMembershipStateRowsQuery,
        GetNegativeMembershipStateRowsResult>
{
    public async Task<GetNegativeMembershipStateRowsResult> ExecuteAsync(
        GetNegativeMembershipStateRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetNegativeMembershipStateRowsResult.Denied();
        }

        var validationFailure = Validate(query);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var hasUnavailableCandidateState = await dbContext
            .Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .Where(membership =>
                membership.Status == MembershipQuerySupport.ActiveMembershipStatus)
            .AnyAsync(
                membership => !dbContext.Set<MembershipStateCacheRecord>()
                    .Any(cache => cache.MembershipId == membership.Id
                        && cache.RecalculationVersion
                            == MembershipStateCacheRebuilder.CurrentRecalculationVersion),
                cancellationToken);
        if (hasUnavailableCandidateState)
        {
            return GetNegativeMembershipStateRowsResult.RecalculationFailed();
        }

        var storageRows = await (
            from cache in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
            join membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
                on cache.MembershipId equals membership.Id
            join client in dbContext.Set<ClientRecord>().AsNoTracking()
                on membership.ClientId equals client.Id
            where membership.Status == MembershipQuerySupport.ActiveMembershipStatus
                && cache.RecalculationVersion
                    == MembershipStateCacheRebuilder.CurrentRecalculationVersion
                && cache.NegativeBalance > 0
            orderby cache.NegativeBalance descending,
                cache.FirstNegativeVisitDate,
                client.NormalizedFullName,
                membership.Id
            select new NegativeMembershipStateStorageRow(
                membership,
                cache,
                client.Surname,
                client.Name,
                client.Patronymic,
                client.PhoneRaw))
            .Skip(query.Offset)
            .Take(query.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = storageRows.Length > query.Limit;
        var visibleRows = storageRows.Take(query.Limit).ToArray();
        var membershipIds = visibleRows
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
        var sourceRows = new List<NegativeMembershipStateSourceRow>(
            visibleRows.Length);

        foreach (var row in visibleRows)
        {
            if (!MembershipQuerySupport.TryMapLifecycleStatus(
                    row.Membership.Status,
                    out var lifecycleStatus)
                || lifecycleStatus != IssuedMembershipLifecycleStatus.Active
                || !MembershipStateReadModelFactory.TryCreate(
                    row.Membership,
                    row.Cache,
                    query.AsOfDate,
                    extensionRowsByMembershipId[row.Membership.Id],
                    out var state))
            {
                return GetNegativeMembershipStateRowsResult.RecalculationFailed();
            }

            try
            {
                sourceRows.Add(new NegativeMembershipStateSourceRow(
                    ClientQuerySupport.BuildDisplayName(
                        row.ClientSurname,
                        row.ClientName,
                        row.ClientPatronymic),
                    row.ClientPhone,
                    lifecycleStatus,
                    state));
            }
            catch (ArgumentException)
            {
                return GetNegativeMembershipStateRowsResult.InconsistentSource();
            }
        }

        try
        {
            return GetNegativeMembershipStateRowsResult.Succeeded(
                new NegativeMembershipStateRowsPage(
                    query.AsOfDate,
                    query.Offset,
                    sourceRows,
                    hasMore));
        }
        catch (ArgumentException)
        {
            return GetNegativeMembershipStateRowsResult.InconsistentSource();
        }
    }

    private static GetNegativeMembershipStateRowsResult? Validate(
        GetNegativeMembershipStateRowsQuery query)
    {
        if (query.AsOfDate == default)
        {
            return GetNegativeMembershipStateRowsResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        if (query.Limit is < 1 or > GetNegativeMembershipStateRowsQuery.MaxLimit)
        {
            return GetNegativeMembershipStateRowsResult.Invalid(
                $"Limit must be between 1 and {GetNegativeMembershipStateRowsQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > GetNegativeMembershipStateRowsQuery.MaxOffset)
        {
            return GetNegativeMembershipStateRowsResult.Invalid(
                $"Offset must be between 0 and {GetNegativeMembershipStateRowsQuery.MaxOffset}.",
                "offset");
        }

        return null;
    }

    private sealed record NegativeMembershipStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord Cache,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        string? ClientPhone);
}
