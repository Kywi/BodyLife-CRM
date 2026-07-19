using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetEndingSoonMembershipStateRowsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<
        GetEndingSoonMembershipStateRowsQuery,
        GetEndingSoonMembershipStateRowsResult>
{
    public async Task<GetEndingSoonMembershipStateRowsResult> ExecuteAsync(
        GetEndingSoonMembershipStateRowsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetEndingSoonMembershipStateRowsResult.Denied();
        }

        var validationFailure = Validate(query);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var throughDate = query.AsOfDate.AddDays(query.DaysThreshold);
        var hasUnavailableCandidateState = await dbContext
            .Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .Where(membership =>
                membership.Status == MembershipQuerySupport.ActiveMembershipStatus
                && membership.BaseEndDate <= throughDate)
            .AnyAsync(
                membership => !dbContext.Set<MembershipStateCacheRecord>()
                    .Any(cache => cache.MembershipId == membership.Id
                        && cache.RecalculationVersion
                            == MembershipStateCacheRebuilder.CurrentRecalculationVersion),
                cancellationToken);
        if (hasUnavailableCandidateState)
        {
            return GetEndingSoonMembershipStateRowsResult.RecalculationFailed();
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
                && cache.EffectiveEndDate >= query.AsOfDate
                && cache.EffectiveEndDate <= throughDate
            orderby cache.EffectiveEndDate,
                client.NormalizedFullName,
                membership.Id
            select new EndingSoonMembershipStateStorageRow(
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
        var sourceRows = new List<EndingSoonMembershipStateSourceRow>(
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
                return GetEndingSoonMembershipStateRowsResult.RecalculationFailed();
            }

            try
            {
                sourceRows.Add(new EndingSoonMembershipStateSourceRow(
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
                return GetEndingSoonMembershipStateRowsResult.InconsistentSource();
            }
        }

        try
        {
            return GetEndingSoonMembershipStateRowsResult.Succeeded(
                new EndingSoonMembershipStateRowsPage(
                    query.AsOfDate,
                    query.DaysThreshold,
                    query.Offset,
                    sourceRows,
                    hasMore));
        }
        catch (ArgumentException)
        {
            return GetEndingSoonMembershipStateRowsResult.InconsistentSource();
        }
    }

    private static GetEndingSoonMembershipStateRowsResult? Validate(
        GetEndingSoonMembershipStateRowsQuery query)
    {
        if (query.AsOfDate == default)
        {
            return GetEndingSoonMembershipStateRowsResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        if (query.DaysThreshold is < 0
            or > GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold)
        {
            return GetEndingSoonMembershipStateRowsResult.Invalid(
                $"Days threshold must be between 0 and {GetEndingSoonMembershipStateRowsQuery.MaxDaysThreshold}.",
                "daysThreshold");
        }

        if (query.AsOfDate.DayNumber
            > DateOnly.MaxValue.DayNumber - query.DaysThreshold)
        {
            return GetEndingSoonMembershipStateRowsResult.Invalid(
                "As-of date and days threshold exceed the supported calendar range.",
                "asOfDate");
        }

        if (query.Limit is < 1 or > GetEndingSoonMembershipStateRowsQuery.MaxLimit)
        {
            return GetEndingSoonMembershipStateRowsResult.Invalid(
                $"Limit must be between 1 and {GetEndingSoonMembershipStateRowsQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > GetEndingSoonMembershipStateRowsQuery.MaxOffset)
        {
            return GetEndingSoonMembershipStateRowsResult.Invalid(
                $"Offset must be between 0 and {GetEndingSoonMembershipStateRowsQuery.MaxOffset}.",
                "offset");
        }

        return null;
    }

    private sealed record EndingSoonMembershipStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord Cache,
        string ClientSurname,
        string ClientName,
        string? ClientPatronymic,
        string? ClientPhone);
}
