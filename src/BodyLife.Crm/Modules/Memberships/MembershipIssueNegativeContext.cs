namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssueNegativeContext
{
    public MembershipIssueNegativeContext(
        int negativeBalance,
        DateOnly? firstNegativeVisitDate)
        : this(negativeBalance, firstNegativeVisitDate, [])
    {
    }

    public MembershipIssueNegativeContext(
        int negativeBalance,
        DateOnly? firstNegativeVisitDate,
        IEnumerable<MembershipNegativeVisitCoverageCandidate>? openConcreteVisits)
    {
        if (negativeBalance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(negativeBalance),
                negativeBalance,
                "Negative issue context requires a positive negative balance.");
        }

        ArgumentNullException.ThrowIfNull(openConcreteVisits);
        var visits = openConcreteVisits
            .OrderBy(visit => visit.OccurredAt)
            .ThenBy(visit => visit.ConsumptionRecordedAt)
            .ThenBy(visit => visit.VisitId)
            .ThenBy(visit => visit.SourceMembershipId)
            .ToArray();
        if (visits.Any(visit => visit.VisitId == Guid.Empty
                || visit.SourceMembershipId == Guid.Empty
                || visit.OldConsumptionId == Guid.Empty)
            || visits.Select(visit => visit.VisitId).Distinct().Count()
                != visits.Length
            || visits.Length > negativeBalance)
        {
            throw new ArgumentException(
                "Open concrete negative Visits must be unique, complete and fit the negative balance.",
                nameof(openConcreteVisits));
        }

        NegativeBalance = negativeBalance;
        FirstNegativeVisitDate = firstNegativeVisitDate;
        OpenConcreteVisits = Array.AsReadOnly(visits);
        UnknownNegativeBalance = negativeBalance - visits.Length;
    }

    public int NegativeBalance { get; }

    public DateOnly? FirstNegativeVisitDate { get; }

    public IReadOnlyList<MembershipNegativeVisitCoverageCandidate> OpenConcreteVisits { get; }

    public int OpenConcreteVisitCount => OpenConcreteVisits.Count;

    public int UnknownNegativeBalance { get; }

    public Guid? OldestOpenConcreteVisitId => OpenConcreteVisits.Count == 0
        ? null
        : OpenConcreteVisits[0].VisitId;

    public DateOnly? OldestOpenConcreteVisitDate => OpenConcreteVisits.Count == 0
        ? null
        : OpenConcreteVisits[0].BusinessDate;
}
