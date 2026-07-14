namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipStateCalculator
{
    public static MembershipCalculatedState CalculateInitial(MembershipIssueTerms? issueTerms)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        return new MembershipCalculatedState(
            countedVisits: 0,
            remainingVisits: issueTerms.Snapshot.VisitsLimit,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: issueTerms.BaseEndDate,
            lastCountedVisitAt: null);
    }

    public static MembershipCalculatedState CalculateFromOpeningState(
        MembershipIssueTerms? issueTerms,
        MembershipOpeningState? openingState)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(openingState);

        if (openingState.OpeningAsOfDate < issueTerms.StartDate)
        {
            throw new ArgumentException(
                "Opening date cannot precede the issued membership start date.",
                nameof(openingState));
        }

        var extensionDays = ResolveExtensionDays(issueTerms, openingState);
        var effectiveEndDate = CalculateEffectiveEndDate(
            issueTerms.BaseEndDate,
            extensionDays,
            openingState);

        if (openingState.KnownEffectiveEndDate is { } knownEffectiveEndDate
            && knownEffectiveEndDate != effectiveEndDate)
        {
            throw new ArgumentException(
                "Known effective end date and extension days must describe the same state.",
                nameof(openingState));
        }

        if (openingState.OpeningAsOfDate > effectiveEndDate)
        {
            throw new ArgumentException(
                "Opening state must describe a membership active on its opening date.",
                nameof(openingState));
        }

        // Missing historical visits stay unknown instead of becoming synthetic source facts.
        return new MembershipCalculatedState(
            countedVisits: 0,
            remainingVisits: openingState.DeclaredRemainingVisits,
            negativeBalance: openingState.DeclaredNegativeBalance,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays,
            effectiveEndDate,
            lastCountedVisitAt: null);
    }

    public static MembershipCalculatedState CalculateFromVisitFacts(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        IEnumerable<MembershipVisitSourceFact>? visitFacts)
    {
        return ApplyVisitFacts(
            membershipId,
            CalculateInitial(issueTerms),
            visitFacts,
            nameof(visitFacts));
    }

    public static MembershipCalculatedState CalculateFromOpeningStateAndVisitFacts(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        MembershipOpeningState? openingState,
        IEnumerable<MembershipVisitSourceFact>? visitFactsNotIncludedInOpeningState)
    {
        return ApplyVisitFacts(
            membershipId,
            CalculateFromOpeningState(issueTerms, openingState),
            visitFactsNotIncludedInOpeningState,
            nameof(visitFactsNotIncludedInOpeningState));
    }

    public static MembershipCalculatedState ApplyExtensionCalculation(
        MembershipIssueTerms? issueTerms,
        MembershipCalculatedState? baseline,
        MembershipExtensionCalculation? extensionCalculation)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(extensionCalculation);

        if (baseline.ExtensionDays != 0
            || baseline.EffectiveEndDate != issueTerms.BaseEndDate)
        {
            throw new ArgumentException(
                "Membership baseline must be extension-free and match the issued base end date.",
                nameof(baseline));
        }

        var effectiveEndDayNumber = (long)issueTerms.BaseEndDate.DayNumber
            + extensionCalculation.ExtensionDays;
        if (effectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extensionCalculation),
                extensionCalculation.ExtensionDays,
                "Extension days exceed the supported calendar range.");
        }

        return new MembershipCalculatedState(
            baseline.CountedVisits,
            baseline.RemainingVisits,
            baseline.NegativeBalance,
            baseline.FirstNegativeVisitId,
            baseline.FirstNegativeVisitDate,
            extensionCalculation.ExtensionDays,
            DateOnly.FromDayNumber((int)effectiveEndDayNumber),
            baseline.LastCountedVisitAt);
    }

    private static int ResolveExtensionDays(
        MembershipIssueTerms issueTerms,
        MembershipOpeningState openingState)
    {
        if (openingState.KnownExtensionDays is { } knownExtensionDays)
        {
            return knownExtensionDays;
        }

        if (openingState.KnownEffectiveEndDate is not { } knownEffectiveEndDate)
        {
            return 0;
        }

        var extensionDays = knownEffectiveEndDate.DayNumber - issueTerms.BaseEndDate.DayNumber;
        if (extensionDays < 0)
        {
            throw new ArgumentException(
                "Known effective end date cannot precede the canonical base end date.",
                nameof(openingState));
        }

        return extensionDays;
    }

    private static DateOnly CalculateEffectiveEndDate(
        DateOnly baseEndDate,
        int extensionDays,
        MembershipOpeningState openingState)
    {
        var effectiveEndDayNumber = (long)baseEndDate.DayNumber + extensionDays;
        if (effectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentException(
                "Known extension days exceed the supported calendar range.",
                nameof(openingState));
        }

        return DateOnly.FromDayNumber((int)effectiveEndDayNumber);
    }

    private static MembershipCalculatedState ApplyVisitFacts(
        Guid membershipId,
        MembershipCalculatedState baseline,
        IEnumerable<MembershipVisitSourceFact>? visitFacts,
        string parameterName)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(visitFacts, parameterName);

        var facts = visitFacts.ToArray();
        var visitIds = new HashSet<Guid>();
        foreach (var fact in facts)
        {
            if (fact is null)
            {
                throw new ArgumentException(
                    "Membership Visit facts cannot contain a missing item.",
                    parameterName);
            }

            if (fact.MembershipId != membershipId)
            {
                throw new ArgumentException(
                    "Membership Visit facts must belong to the selected membership.",
                    parameterName);
            }

            if (!visitIds.Add(fact.VisitId))
            {
                throw new ArgumentException(
                    "Each Membership Visit source id must be unique.",
                    parameterName);
            }
        }

        var activeFacts = facts
            .Where(fact => fact.IsActiveCounted)
            .OrderBy(fact => fact.OccurredAt)
            .ThenBy(fact => fact.RecordedAt)
            .ThenBy(fact => fact.VisitId)
            .ToArray();
        var countedVisits = (long)baseline.CountedVisits + activeFacts.Length;
        var remainingVisits = (long)baseline.RemainingVisits - activeFacts.Length;
        var negativeBalance = Math.Max(0L, -remainingVisits);

        if (countedVisits > int.MaxValue
            || remainingVisits < int.MinValue
            || remainingVisits > int.MaxValue
            || negativeBalance > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Membership Visit facts exceed the supported calculated-state range.");
        }

        var firstNegativeVisitId = baseline.FirstNegativeVisitId;
        var firstNegativeVisitDate = baseline.FirstNegativeVisitDate;
        var runningRemainingVisits = (long)baseline.RemainingVisits;
        foreach (var fact in activeFacts)
        {
            var previousRemainingVisits = runningRemainingVisits;
            runningRemainingVisits--;

            if (firstNegativeVisitId is null
                && firstNegativeVisitDate is null
                && previousRemainingVisits >= 0
                && runningRemainingVisits < 0)
            {
                firstNegativeVisitId = fact.VisitId;
                firstNegativeVisitDate = fact.BusinessDate;
            }
        }

        var lastCountedVisitAt = baseline.LastCountedVisitAt;
        if (activeFacts.Length > 0
            && (lastCountedVisitAt is null
                || activeFacts[^1].OccurredAt > lastCountedVisitAt.Value))
        {
            lastCountedVisitAt = activeFacts[^1].OccurredAt;
        }

        return new MembershipCalculatedState(
            (int)countedVisits,
            (int)remainingVisits,
            (int)negativeBalance,
            firstNegativeVisitId,
            firstNegativeVisitDate,
            baseline.ExtensionDays,
            baseline.EffectiveEndDate,
            lastCountedVisitAt);
    }
}
