namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssuePreparation
{
    internal MembershipIssuePreparation(MembershipIssuePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (!preview.CanProceedToIssue)
        {
            throw new ArgumentException(
                "Membership issue preview must be eligible to proceed.",
                nameof(preview));
        }

        var warningItems = preview.Warnings.ToArray();

        ClientId = preview.ClientId;
        MembershipTypeId = preview.MembershipTypeId;
        Snapshot = preview.Snapshot;
        StartDate = preview.ProposedStartDate;
        BaseEndDate = preview.BaseEndDate;
        ExpectedInitialState = preview.ExpectedInitialState;
        ExistingNegativeState = preview.ExistingNegativeState;
        NegativeHandlingDecision = preview.SelectedNegativeHandlingDecision;
        Warnings = Array.AsReadOnly(warningItems);
    }

    public Guid ClientId { get; }

    public Guid MembershipTypeId { get; }

    public IssuedMembershipSnapshot Snapshot { get; }

    public DateOnly StartDate { get; }

    public DateOnly BaseEndDate { get; }

    public MembershipCalculatedState ExpectedInitialState { get; }

    public MembershipIssueNegativeContext? ExistingNegativeState { get; }

    public MembershipNegativeHandlingDecision? NegativeHandlingDecision { get; }

    public IReadOnlyList<MembershipWarning> Warnings { get; }
}
