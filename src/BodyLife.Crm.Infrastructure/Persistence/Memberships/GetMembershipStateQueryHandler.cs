using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class GetMembershipStateQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetMembershipStateQuery, GetMembershipStateResult>
{
    public async Task<GetMembershipStateResult> ExecuteAsync(
        GetMembershipStateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetMembershipStateResult.Denied();
        }

        if (query.MembershipId == Guid.Empty)
        {
            return GetMembershipStateResult.Invalid(
                "Membership id is required.",
                "membershipId");
        }

        if (query.AsOfDate == default)
        {
            return GetMembershipStateResult.Invalid(
                "As-of date is required.",
                "asOfDate");
        }

        var row = await (
            from membership in dbContext.Set<IssuedMembershipRecord>().AsNoTracking()
            join cache in dbContext.Set<MembershipStateCacheRecord>().AsNoTracking()
                on membership.Id equals cache.MembershipId
            where membership.Id == query.MembershipId
            select new MembershipStateStorageRow(
                membership,
                cache,
                dbContext.Set<MembershipOpeningStateRecord>().Any(
                    openingState => openingState.MembershipId == membership.Id
                        && openingState.Status == MembershipQuerySupport.ActiveOpeningStateStatus)))
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            var membershipExists = await dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                .AnyAsync(
                    membership => membership.Id == query.MembershipId,
                    cancellationToken);

            return membershipExists
                ? GetMembershipStateResult.RecalculationFailed()
                : GetMembershipStateResult.Missing();
        }

        var extensionRows = await dbContext.Set<MembershipExtensionDayRecord>()
            .AsNoTracking()
            .Where(extensionDay => extensionDay.MembershipId == query.MembershipId)
            .ToArrayAsync(cancellationToken);

        if (!MembershipStateReadModelFactory.TryCreate(
                row.Membership,
                row.Cache,
                query.AsOfDate,
                extensionRows,
                out var readModel))
        {
            return GetMembershipStateResult.RecalculationFailed();
        }

        return GetMembershipStateResult.Succeeded(
            readModel,
            MembershipQuerySupport.BuildActionPermissions(
                row.Membership.Status,
                row.HasActiveOpeningState));
    }

    private sealed record MembershipStateStorageRow(
        IssuedMembershipRecord Membership,
        MembershipStateCacheRecord Cache,
        bool HasActiveOpeningState);
}
