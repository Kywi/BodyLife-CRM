using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
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
            select new MembershipStateRow(
                membership.Id,
                membership.ClientId,
                membership.MembershipTypeId,
                membership.TypeNameSnapshot,
                membership.DurationDaysSnapshot,
                membership.VisitsLimitSnapshot,
                membership.PriceAmountSnapshot,
                membership.PriceCurrencySnapshot,
                membership.StartDate,
                membership.BaseEndDate,
                membership.Status,
                cache.CountedVisits,
                cache.RemainingVisits,
                cache.NegativeBalance,
                cache.FirstNegativeVisitId,
                cache.FirstNegativeVisitDate,
                cache.ExtensionDays,
                cache.EffectiveEndDate,
                cache.LastCountedVisitAt,
                cache.RecalculationVersion,
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

        if (row.RecalculationVersion != MembershipStateCacheRebuilder.CurrentRecalculationVersion)
        {
            return GetMembershipStateResult.RecalculationFailed();
        }

        var extensionRows = await dbContext.Set<MembershipExtensionDayRecord>()
            .AsNoTracking()
            .Where(extensionDay => extensionDay.MembershipId == query.MembershipId)
            .OrderBy(extensionDay => extensionDay.ExtensionDate)
            .ThenByDescending(extensionDay => extensionDay.IsActive)
            .ThenBy(extensionDay => extensionDay.SourceType)
            .ThenBy(extensionDay => extensionDay.SourceId)
            .ThenBy(extensionDay => extensionDay.SourceLabel)
            .Select(extensionDay => new MembershipExtensionRow(
                extensionDay.ExtensionDate,
                extensionDay.SourceType,
                extensionDay.SourceId,
                extensionDay.SourceLabel,
                extensionDay.IsActive))
            .ToArrayAsync(cancellationToken);

        MembershipStateReadModel readModel;

        try
        {
            var snapshot = new IssuedMembershipSnapshot(
                row.TypeNameSnapshot,
                row.DurationDaysSnapshot,
                row.VisitsLimitSnapshot,
                new Money(row.PriceAmountSnapshot, row.PriceCurrencySnapshot));
            var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
                row.MembershipTypeId,
                snapshot,
                row.StartDate,
                row.BaseEndDate);
            var calculatedState = MembershipCalculatedState.FromStoredCache(
                issueTerms,
                row.CountedVisits,
                row.RemainingVisits,
                row.NegativeBalance,
                row.FirstNegativeVisitId,
                row.FirstNegativeVisitDate,
                row.ExtensionDays,
                row.EffectiveEndDate,
                row.LastCountedVisitAt);
            var extensionExplanation = extensionRows
                .Select(extensionDay => MembershipExtensionDay.FromStoredExplanation(
                    extensionDay.ExtensionDate,
                    extensionDay.SourceType,
                    extensionDay.SourceId,
                    extensionDay.SourceLabel,
                    extensionDay.IsActive))
                .ToArray();
            readModel = new MembershipStateReadModel(
                row.MembershipId,
                row.ClientId,
                issueTerms,
                calculatedState,
                query.AsOfDate,
                extensionExplanation);
        }
        catch (ArgumentException)
        {
            return GetMembershipStateResult.RecalculationFailed();
        }

        return GetMembershipStateResult.Succeeded(
            readModel,
            MembershipQuerySupport.BuildActionPermissions(
                row.MembershipStatus,
                row.HasActiveOpeningState));
    }

    private sealed record MembershipStateRow(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId,
        string TypeNameSnapshot,
        int DurationDaysSnapshot,
        int VisitsLimitSnapshot,
        decimal PriceAmountSnapshot,
        string PriceCurrencySnapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        string MembershipStatus,
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        int RecalculationVersion,
        bool HasActiveOpeningState);

    private sealed record MembershipExtensionRow(
        DateOnly ExtensionDate,
        string SourceType,
        Guid SourceId,
        string SourceLabel,
        bool IsActive);
}
