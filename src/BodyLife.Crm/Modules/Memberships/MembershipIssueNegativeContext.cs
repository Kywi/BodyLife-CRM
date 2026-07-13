namespace BodyLife.Crm.Modules.Memberships;

public sealed class MembershipIssueNegativeContext
{
    public MembershipIssueNegativeContext(
        int negativeBalance,
        DateOnly? firstNegativeVisitDate)
    {
        if (negativeBalance <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(negativeBalance),
                negativeBalance,
                "Negative issue context requires a positive negative balance.");
        }

        NegativeBalance = negativeBalance;
        FirstNegativeVisitDate = firstNegativeVisitDate;
    }

    public int NegativeBalance { get; }

    public DateOnly? FirstNegativeVisitDate { get; }
}
