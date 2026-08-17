using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipIssuePreviewPolicy
{
    public static MembershipIssuePreview Create(
        Guid clientId,
        MembershipTypeCatalogItem? membershipType,
        DateOnly proposedStartDate,
        MembershipIssueNegativeContext? existingNegativeState = null,
        DateOnly? previewBusinessDate = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        ArgumentNullException.ThrowIfNull(membershipType);
        if (previewBusinessDate is { } date
            && !BusinessTimeZone.IsSupportedBusinessDate(date))
        {
            throw new ArgumentOutOfRangeException(nameof(previewBusinessDate));
        }

        var automaticCount = existingNegativeState is { OpenConcreteVisitCount: > 0 }
            ? Math.Min(existingNegativeState.OpenConcreteVisitCount, membershipType.VisitsLimit)
            : 0;
        var terms = MembershipIssueTerms.FromActiveMembershipType(
            membershipType,
            automaticCount > 0
                ? existingNegativeState!.OldestOpenConcreteVisitDate!.Value
                : proposedStartDate);
        var expectedState = automaticCount > 0
            ? MembershipStateCalculator.CalculateInitialWithCoveredVisits(
                terms,
                existingNegativeState!.OpenConcreteVisits.Take(automaticCount))
            : MembershipStateCalculator.CalculateInitial(terms);
        var warnings = new List<MembershipWarning>();
        if (existingNegativeState is not null)
        {
            var remainingConcrete = existingNegativeState.OpenConcreteVisitCount - automaticCount;
            if (remainingConcrete > 0)
            {
                warnings.Add(new(
                    MembershipWarningCodes.NegativeBalance,
                    MembershipWarningSeverity.Danger,
                    "Some concrete negative Visits remain uncovered."));
            }
            else if (existingNegativeState.UnknownNegativeBalance > 0)
            {
                warnings.Add(new(
                    MembershipWarningCodes.NegativeBalance,
                    MembershipWarningSeverity.Warning,
                    "An unknown opening or backfill remainder remains visible."));
            }
            if (automaticCount > 0
                && previewBusinessDate is { } current
                && expectedState.EffectiveEndDate < current)
            {
                warnings.Add(new(
                    MembershipWarningCodes.ExpiredByDate,
                    MembershipWarningSeverity.Danger,
                    "The automatically backdated membership will already be expired."));
            }
        }

        return new MembershipIssuePreview(
            clientId,
            membershipType.UpdatedAt,
            terms,
            expectedState,
            existingNegativeState,
            automaticCount,
            previewBusinessDate,
            warnings);
    }
}
