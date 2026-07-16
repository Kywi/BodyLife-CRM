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

    public static MembershipCalculatedState CalculateFromAdjustmentFacts(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        IEnumerable<MembershipAdjustmentSourceFact>? adjustmentFacts)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        return ApplyAdjustmentFacts(
            membershipId,
            issueTerms,
            CalculateInitial(issueTerms),
            adjustmentFacts,
            nameof(adjustmentFacts));
    }

    public static MembershipCalculatedState CalculateFromOpeningStateAndAdjustmentFacts(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        MembershipOpeningState? openingState,
        IEnumerable<MembershipAdjustmentSourceFact>? adjustmentFactsNotIncludedInOpeningState)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        return ApplyAdjustmentFacts(
            membershipId,
            issueTerms,
            CalculateFromOpeningState(issueTerms, openingState),
            adjustmentFactsNotIncludedInOpeningState,
            nameof(adjustmentFactsNotIncludedInOpeningState));
    }

    public static MembershipCalculatedState CalculateFromVisitAndAdjustmentFacts(
        Guid membershipId,
        MembershipIssueTerms? issueTerms,
        IEnumerable<MembershipVisitSourceFact>? visitFacts,
        IEnumerable<MembershipAdjustmentSourceFact>? adjustmentFacts)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        var adjustedBaseline = ApplyAdjustmentFacts(
            membershipId,
            issueTerms,
            CalculateInitial(issueTerms),
            adjustmentFacts,
            nameof(adjustmentFacts));

        return ApplyVisitFacts(
            membershipId,
            adjustedBaseline,
            visitFacts,
            nameof(visitFacts));
    }

    public static MembershipCalculatedState
        CalculateFromOpeningStateVisitAndAdjustmentFacts(
            Guid membershipId,
            MembershipIssueTerms? issueTerms,
            MembershipOpeningState? openingState,
            IEnumerable<MembershipVisitSourceFact>? visitFactsNotIncludedInOpeningState,
            IEnumerable<MembershipAdjustmentSourceFact>?
                adjustmentFactsNotIncludedInOpeningState)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);

        var adjustedBaseline = ApplyAdjustmentFacts(
            membershipId,
            issueTerms,
            CalculateFromOpeningState(issueTerms, openingState),
            adjustmentFactsNotIncludedInOpeningState,
            nameof(adjustmentFactsNotIncludedInOpeningState));

        return ApplyVisitFacts(
            membershipId,
            adjustedBaseline,
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

    public static MembershipCalculatedState ApplyDateRangeExtensionCalculation(
        MembershipIssueTerms? issueTerms,
        MembershipCalculatedState? sourceBaseline,
        MembershipExtensionCalculation? extensionCalculation)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(sourceBaseline);
        ArgumentNullException.ThrowIfNull(extensionCalculation);

        MembershipCalculatedState canonicalBaseline;
        try
        {
            canonicalBaseline = MembershipCalculatedState.FromStoredCache(
                issueTerms,
                sourceBaseline.CountedVisits,
                sourceBaseline.RemainingVisits,
                sourceBaseline.NegativeBalance,
                sourceBaseline.FirstNegativeVisitId,
                sourceBaseline.FirstNegativeVisitDate,
                sourceBaseline.ExtensionDays,
                sourceBaseline.EffectiveEndDate,
                sourceBaseline.LastCountedVisitAt);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "Membership source baseline must match the supplied issued terms.",
                nameof(sourceBaseline),
                exception);
        }

        // The source baseline excludes date-range sources; the calculation is their full union.
        var extensionDays = (long)canonicalBaseline.ExtensionDays
            + extensionCalculation.ExtensionDays;
        var effectiveEndDayNumber = (long)issueTerms.BaseEndDate.DayNumber
            + extensionDays;
        if (extensionDays > int.MaxValue
            || effectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extensionCalculation),
                extensionCalculation.ExtensionDays,
                "Extension days exceed the supported calendar range.");
        }

        return new MembershipCalculatedState(
            canonicalBaseline.CountedVisits,
            canonicalBaseline.RemainingVisits,
            canonicalBaseline.NegativeBalance,
            canonicalBaseline.FirstNegativeVisitId,
            canonicalBaseline.FirstNegativeVisitDate,
            (int)extensionDays,
            DateOnly.FromDayNumber((int)effectiveEndDayNumber),
            canonicalBaseline.LastCountedVisitAt);
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

    private static MembershipCalculatedState ApplyAdjustmentFacts(
        Guid membershipId,
        MembershipIssueTerms issueTerms,
        MembershipCalculatedState baseline,
        IEnumerable<MembershipAdjustmentSourceFact>? adjustmentFacts,
        string parameterName)
    {
        if (membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(adjustmentFacts, parameterName);

        var adjustmentIds = new HashSet<Guid>();
        long extensionDaysDelta = 0;
        long visitsDelta = 0;
        foreach (var fact in adjustmentFacts)
        {
            if (fact is null)
            {
                throw new ArgumentException(
                    "Membership adjustment facts cannot contain a missing item.",
                    parameterName);
            }

            if (fact.MembershipId != membershipId)
            {
                throw new ArgumentException(
                    "Membership adjustment facts must belong to the selected membership.",
                    parameterName);
            }

            if (!adjustmentIds.Add(fact.AdjustmentId))
            {
                throw new ArgumentException(
                    "Each Membership adjustment source id must be unique.",
                    parameterName);
            }

            if (!fact.IsActive)
            {
                continue;
            }

            switch (fact.AdjustmentType)
            {
                case MembershipAdjustmentTypes.ExtensionDays
                    when fact.DaysDelta is > 0
                        && fact.VisitsDelta is null
                        && fact.MoneyDelta is null:
                    extensionDaysDelta += fact.DaysDelta.Value;
                    break;

                case MembershipAdjustmentTypes.VisitBalance
                    when fact.VisitsDelta is not null and not 0
                        && fact.DaysDelta is null
                        && fact.MoneyDelta is null:
                    visitsDelta += fact.VisitsDelta.Value;
                    break;

                default:
                    throw new ArgumentException(
                        "Active Membership adjustment type or delta shape is not supported.",
                        parameterName);
            }
        }

        var extensionDays = (long)baseline.ExtensionDays + extensionDaysDelta;
        var remainingVisits = (long)baseline.RemainingVisits + visitsDelta;
        var negativeBalance = Math.Max(0L, -remainingVisits);
        var effectiveEndDayNumber = (long)issueTerms.BaseEndDate.DayNumber + extensionDays;

        if (extensionDays > int.MaxValue
            || remainingVisits < int.MinValue
            || remainingVisits > int.MaxValue
            || negativeBalance > int.MaxValue
            || effectiveEndDayNumber > DateOnly.MaxValue.DayNumber)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Membership adjustment facts exceed the supported calculated-state range.");
        }

        return new MembershipCalculatedState(
            baseline.CountedVisits,
            (int)remainingVisits,
            (int)negativeBalance,
            remainingVisits < 0 ? baseline.FirstNegativeVisitId : null,
            remainingVisits < 0 ? baseline.FirstNegativeVisitDate : null,
            (int)extensionDays,
            DateOnly.FromDayNumber((int)effectiveEndDayNumber),
            baseline.LastCountedVisitAt);
    }
}
