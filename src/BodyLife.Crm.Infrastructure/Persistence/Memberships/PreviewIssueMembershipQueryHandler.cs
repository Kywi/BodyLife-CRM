using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class PreviewIssueMembershipQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<PreviewIssueMembershipQuery, PreviewIssueMembershipResult>
{
    public async Task<PreviewIssueMembershipResult> ExecuteAsync(
        PreviewIssueMembershipQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return PreviewIssueMembershipResult.Denied();
        }

        if (query.ClientId == Guid.Empty)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.MembershipTypeId == Guid.Empty)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Membership type id is required.",
                "membershipTypeId");
        }

        if (query.ProposedStartDate == default)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Proposed start date is required.",
                "proposedStartDate");
        }

        if (query.NegativeHandlingDecision is { } decision
            && !Enum.IsDefined(decision))
        {
            return PreviewIssueMembershipResult.Invalid(
                "Negative handling decision is not supported.",
                "negativeHandlingDecision");
        }

        var clientExists = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return PreviewIssueMembershipResult.MissingClient();
        }

        var membershipType = await dbContext.Set<MembershipTypeRecord>()
            .AsNoTracking()
            .Where(record => record.Id == query.MembershipTypeId)
            .Select(record => new MembershipTypeRow(
                record.Id,
                record.Name,
                record.DurationDays,
                record.VisitsLimit,
                record.PriceAmount,
                record.PriceCurrency,
                record.IsActive,
                record.Comment,
                record.CreatedAt,
                record.UpdatedAt,
                record.DeactivatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (membershipType is null)
        {
            return PreviewIssueMembershipResult.MissingMembershipType();
        }

        if (!membershipType.IsActive)
        {
            return PreviewIssueMembershipResult.InactiveMembershipType();
        }

        var activeMemberships = await dbContext.Set<IssuedMembershipRecord>()
            .AsNoTracking()
            .Where(membership =>
                membership.ClientId == query.ClientId
                && membership.Status == MembershipQuerySupport.ActiveMembershipStatus)
            .OrderByDescending(membership => membership.StartDate)
            .ThenByDescending(membership => membership.IssuedAt)
            .ThenBy(membership => membership.Id)
            .Select(membership => new ExistingMembershipRow(
                membership.Id,
                membership.MembershipTypeId,
                membership.TypeNameSnapshot,
                membership.DurationDaysSnapshot,
                membership.VisitsLimitSnapshot,
                membership.PriceAmountSnapshot,
                membership.PriceCurrencySnapshot,
                membership.StartDate,
                membership.BaseEndDate))
            .ToArrayAsync(cancellationToken);
        var activeMembershipIds = activeMemberships
            .Select(membership => membership.MembershipId)
            .ToArray();
        ExistingStateCacheRow[] cacheRows = activeMembershipIds.Length == 0
            ? []
            : await dbContext.Set<MembershipStateCacheRecord>()
                .AsNoTracking()
                .Where(cache => activeMembershipIds.Contains(cache.MembershipId))
                .Select(cache => new ExistingStateCacheRow(
                    cache.MembershipId,
                    cache.CountedVisits,
                    cache.RemainingVisits,
                    cache.NegativeBalance,
                    cache.FirstNegativeVisitId,
                    cache.FirstNegativeVisitDate,
                    cache.ExtensionDays,
                    cache.EffectiveEndDate,
                    cache.LastCountedVisitAt,
                    cache.RecalculationVersion))
                .ToArrayAsync(cancellationToken);
        var cachesByMembershipId = cacheRows.ToDictionary(
            cache => cache.MembershipId);
        var negativeStates = new List<MembershipIssueNegativeContext>(1);

        foreach (var membership in activeMemberships)
        {
            if (!cachesByMembershipId.TryGetValue(membership.MembershipId, out var cache)
                || cache.RecalculationVersion
                    != MembershipStateCacheRebuilder.CurrentRecalculationVersion)
            {
                return PreviewIssueMembershipResult.RecalculationFailed();
            }

            MembershipCalculatedState calculatedState;

            try
            {
                var snapshot = new IssuedMembershipSnapshot(
                    membership.TypeNameSnapshot,
                    membership.DurationDaysSnapshot,
                    membership.VisitsLimitSnapshot,
                    new Money(
                        membership.PriceAmountSnapshot,
                        membership.PriceCurrencySnapshot));
                var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
                    membership.MembershipTypeId,
                    snapshot,
                    membership.StartDate,
                    membership.BaseEndDate);
                calculatedState = MembershipCalculatedState.FromStoredCache(
                    issueTerms,
                    cache.CountedVisits,
                    cache.RemainingVisits,
                    cache.NegativeBalance,
                    cache.FirstNegativeVisitId,
                    cache.FirstNegativeVisitDate,
                    cache.ExtensionDays,
                    cache.EffectiveEndDate,
                    cache.LastCountedVisitAt);
            }
            catch (ArgumentException)
            {
                return PreviewIssueMembershipResult.RecalculationFailed();
            }

            if (calculatedState.NegativeBalance > 0)
            {
                negativeStates.Add(new MembershipIssueNegativeContext(
                    calculatedState.NegativeBalance,
                    calculatedState.FirstNegativeVisitDate));
            }
        }

        if (negativeStates.Count > 1)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Multiple active memberships have negative balances. Explicit membership selection is required.",
                "clientId");
        }

        var existingNegativeState = negativeStates.SingleOrDefault();
        if (existingNegativeState is null && query.NegativeHandlingDecision is not null)
        {
            return PreviewIssueMembershipResult.Invalid(
                "A negative handling decision requires existing negative membership state.",
                "negativeHandlingDecision");
        }

        MembershipIssuePreview preview;

        try
        {
            var catalogItem = new MembershipTypeCatalogItem(
                membershipType.MembershipTypeId,
                membershipType.Name,
                membershipType.DurationDays,
                membershipType.VisitsLimit,
                new Money(membershipType.PriceAmount, membershipType.PriceCurrency),
                membershipType.IsActive,
                membershipType.Comment,
                membershipType.CreatedAt,
                membershipType.UpdatedAt,
                membershipType.DeactivatedAt);
            preview = MembershipIssuePreviewPolicy.Create(
                query.ClientId,
                catalogItem,
                query.ProposedStartDate,
                existingNegativeState,
                query.NegativeHandlingDecision);
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "durationDays")
        {
            return PreviewIssueMembershipResult.Invalid(
                "Proposed start date and membership duration exceed the supported calendar range.",
                "proposedStartDate");
        }
        catch (ArgumentException)
        {
            return PreviewIssueMembershipResult.Invalid(
                "Membership type data cannot produce a valid issue preview.",
                "membershipTypeId");
        }
        catch (InvalidOperationException)
        {
            return PreviewIssueMembershipResult.InactiveMembershipType();
        }

        return PreviewIssueMembershipResult.Succeeded(
            preview,
            MembershipQuerySupport.BuildIssueActionPermissions());
    }

    private sealed record MembershipTypeRow(
        Guid MembershipTypeId,
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt);

    private sealed record ExistingMembershipRow(
        Guid MembershipId,
        Guid MembershipTypeId,
        string TypeNameSnapshot,
        int DurationDaysSnapshot,
        int VisitsLimitSnapshot,
        decimal PriceAmountSnapshot,
        string PriceCurrencySnapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate);

    private sealed record ExistingStateCacheRow(
        Guid MembershipId,
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        int RecalculationVersion);
}
