using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetLowRemainingMembershipStateRowsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetLowRemainingMembershipStateRowsQuery,
        GetLowRemainingMembershipStateRowsResult>
{
    public async Task<GetLowRemainingMembershipStateRowsResult> ExecuteAsync(
        GetLowRemainingMembershipStateRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetLowRemainingMembershipStateRowsResult.Denied();
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
            return GetLowRemainingMembershipStateRowsResult.RecalculationFailed();
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
                && cache.RemainingVisits <= query.RemainingVisitsThreshold
            orderby cache.RemainingVisits,
                client.NormalizedFullName,
                membership.Id
            select new LowRemainingMembershipStateStorageRow(
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
        var sourceRows = new List<LowRemainingMembershipStateSourceRow>(
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
                return GetLowRemainingMembershipStateRowsResult.RecalculationFailed();
            }

            try
            {
                sourceRows.Add(new LowRemainingMembershipStateSourceRow(
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
                return GetLowRemainingMembershipStateRowsResult.InconsistentSource();
            }
        }

        try
        {
            return GetLowRemainingMembershipStateRowsResult.Succeeded(
                new LowRemainingMembershipStateRowsPage(
                    query.AsOfDate,
                    query.RemainingVisitsThreshold,
                    query.Offset,
                    sourceRows,
                    hasMore));
        }
        catch (ArgumentException)
        {
            return GetLowRemainingMembershipStateRowsResult.InconsistentSource();
        }
    }

    private static GetLowRemainingMembershipStateRowsResult? Validate(
        GetLowRemainingMembershipStateRowsQuery query)
    {
        if (query.AsOfDate == default)
        {
            return GetLowRemainingMembershipStateRowsResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        if (query.RemainingVisitsThreshold < 0)
        {
            return GetLowRemainingMembershipStateRowsResult.Invalid(
                "Remaining-visits threshold cannot be negative.",
                "remainingVisitsThreshold");
        }

        if (query.Limit is < 1 or > GetLowRemainingMembershipStateRowsQuery.MaxLimit)
        {
            return GetLowRemainingMembershipStateRowsResult.Invalid(
                $"Limit must be between 1 and {GetLowRemainingMembershipStateRowsQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > GetLowRemainingMembershipStateRowsQuery.MaxOffset)
        {
            return GetLowRemainingMembershipStateRowsResult.Invalid(
                $"Offset must be between 0 and {GetLowRemainingMembershipStateRowsQuery.MaxOffset}.",
                "offset");
        }

        return null;
    }

    private sealed record LowRemainingMembershipStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord Cache,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        string? ClientPhone);
}
