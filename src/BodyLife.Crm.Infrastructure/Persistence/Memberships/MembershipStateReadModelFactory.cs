using System.Diagnostics.CodeAnalysis;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipStateReadModelFactory
{
    internal static bool TryCreate(
        IssuedMembershipRecord membership,
        MembershipStateCacheRecord cache,
        DateOnly asOfDate,
        IEnumerable<MembershipExtensionDayRecord> extensionRows,
        [NotNullWhen(true)] out MembershipStateReadModel? readModel)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(extensionRows);

        if (cache.MembershipId != membership.Id
            || cache.RecalculationVersion
                != MembershipStateCacheRebuilder.CurrentRecalculationVersion)
        {
            readModel = null;
            return false;
        }

        var storedExtensionRows = extensionRows.ToArray();
        if (storedExtensionRows.Any(extensionDay =>
                extensionDay.MembershipId != membership.Id))
        {
            readModel = null;
            return false;
        }

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
            var calculatedState = MembershipCalculatedState.FromStoredCache(
                issueTerms,
                cache.CountedVisits,
                cache.RemainingVisits,
                cache.NegativeBalance,
                cache.FirstNegativeVisitId,
                cache.FirstNegativeVisitDate,
                cache.ExtensionDays,
                cache.EffectiveEndDate,
                cache.LastCountedVisitAt);
            var extensionExplanation = storedExtensionRows
                .OrderBy(extensionDay => extensionDay.ExtensionDate)
                .ThenByDescending(extensionDay => extensionDay.IsActive)
                .ThenBy(
                    extensionDay => extensionDay.SourceType,
                    StringComparer.Ordinal)
                .ThenBy(extensionDay => extensionDay.SourceId)
                .ThenBy(
                    extensionDay => extensionDay.SourceLabel,
                    StringComparer.Ordinal)
                .Select(extensionDay => MembershipExtensionDay.FromStoredExplanation(
                    extensionDay.ExtensionDate,
                    extensionDay.SourceType,
                    extensionDay.SourceId,
                    extensionDay.SourceLabel,
                    extensionDay.IsActive))
                .ToArray();
            readModel = new MembershipStateReadModel(
                membership.Id,
                membership.ClientId,
                issueTerms,
                calculatedState,
                asOfDate,
                extensionExplanation);
            return true;
        }
        catch (ArgumentException)
        {
            readModel = null;
            return false;
        }
    }
}
