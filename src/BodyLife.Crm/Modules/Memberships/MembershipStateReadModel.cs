namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipStateReadModel
{
    public MembershipStateReadModel(
        Guid membershipId,
        Guid clientId,
        MembershipIssueTerms issueTerms,
        MembershipCalculatedState calculatedState,
        DateOnly asOfDate)
    {
        ArgumentNullException.ThrowIfNull(issueTerms);
        ArgumentNullException.ThrowIfNull(calculatedState);

        MembershipId = membershipId;
        ClientId = clientId;
        MembershipTypeId = issueTerms.MembershipTypeId;
        Snapshot = issueTerms.Snapshot;
        StartDate = issueTerms.StartDate;
        BaseEndDate = issueTerms.BaseEndDate;
        EffectiveEndDate = calculatedState.EffectiveEndDate;
        CountedVisits = calculatedState.CountedVisits;
        RemainingVisits = calculatedState.RemainingVisits;
        NegativeBalance = calculatedState.NegativeBalance;
        FirstNegativeVisitId = calculatedState.FirstNegativeVisitId;
        FirstNegativeVisitDate = calculatedState.FirstNegativeVisitDate;
        ExtensionDays = calculatedState.ExtensionDays;
        LastCountedVisitAt = calculatedState.LastCountedVisitAt;
        AsOfDate = asOfDate;
        Warnings = Array.AsReadOnly(
            MembershipWarningRules.Derive(calculatedState, asOfDate).ToArray());
    }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public Guid MembershipTypeId { get; }

    public IssuedMembershipSnapshot Snapshot { get; }

    public DateOnly StartDate { get; }

    public DateOnly BaseEndDate { get; }

    public DateOnly EffectiveEndDate { get; }

    public int CountedVisits { get; }

    public int RemainingVisits { get; }

    public int NegativeBalance { get; }

    public Guid? FirstNegativeVisitId { get; }

    public DateOnly? FirstNegativeVisitDate { get; }

    public int ExtensionDays { get; }

    public DateTimeOffset? LastCountedVisitAt { get; }

    public DateOnly AsOfDate { get; }

    public IReadOnlyList<MembershipWarning> Warnings { get; }

    public bool IsActiveByDate => MembershipDateRules.IsActiveByDate(
        AsOfDate,
        EffectiveEndDate);
}
