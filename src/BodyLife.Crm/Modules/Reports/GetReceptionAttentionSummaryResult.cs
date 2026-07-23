namespace BodyLife.Crm.Modules.Reports;

public enum GetReceptionAttentionSummaryStatus
{
    Success = 1,
    PermissionDenied,
    ValidationFailed,
    RecalculationFailed,
    SourceInconsistent,
}

public enum ReceptionAttentionDestination
{
    EndingSoon = 1,
    NegativeClients,
}

public sealed class ReceptionAttentionSummary
{
    private ReceptionAttentionSummary(
        int endingSoonMembershipCount,
        int negativeClientCount,
        ReceptionAttentionDestination endingSoonReportDestination,
        ReceptionAttentionDestination negativeClientsReportDestination)
    {
        EndingSoonMembershipCount = endingSoonMembershipCount;
        NegativeClientCount = negativeClientCount;
        EndingSoonReportDestination = endingSoonReportDestination;
        NegativeClientsReportDestination = negativeClientsReportDestination;
    }

    public int EndingSoonMembershipCount { get; }
    public int NegativeClientCount { get; }
    public ReceptionAttentionDestination EndingSoonReportDestination { get; }
    public ReceptionAttentionDestination NegativeClientsReportDestination { get; }

    public static ReceptionAttentionSummary Create(
        int endingSoonMembershipCount,
        int negativeClientCount,
        ReceptionAttentionDestination endingSoonReportDestination,
        ReceptionAttentionDestination negativeClientsReportDestination)
    {
        if (endingSoonMembershipCount < 0 || negativeClientCount < 0
            || !Enum.IsDefined(endingSoonReportDestination)
            || !Enum.IsDefined(negativeClientsReportDestination))
        {
            throw new ArgumentException("Reception attention summary must contain valid non-negative counts and destinations.");
        }

        return new ReceptionAttentionSummary(
            endingSoonMembershipCount,
            negativeClientCount,
            endingSoonReportDestination,
            negativeClientsReportDestination);
    }
}

public sealed record GetReceptionAttentionSummaryResult(
    GetReceptionAttentionSummaryStatus Status,
    ReceptionAttentionSummary? Summary,
    string? ErrorCode,
    string? ErrorMessage,
    string? ErrorField)
{
    public static GetReceptionAttentionSummaryResult Success(ReceptionAttentionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return new(GetReceptionAttentionSummaryStatus.Success, summary, null, null, null);
    }

    public static GetReceptionAttentionSummaryResult PermissionDenied(string message) => Failure(GetReceptionAttentionSummaryStatus.PermissionDenied, "permission_denied", message, null);
    public static GetReceptionAttentionSummaryResult ValidationFailed(string message, string? field) => Failure(GetReceptionAttentionSummaryStatus.ValidationFailed, "validation_failed", message, field);
    public static GetReceptionAttentionSummaryResult RecalculationFailed(string message) => Failure(GetReceptionAttentionSummaryStatus.RecalculationFailed, "recalculation_failed", message, null);
    public static GetReceptionAttentionSummaryResult SourceInconsistent(string message, string? field) => Failure(GetReceptionAttentionSummaryStatus.SourceInconsistent, "source_inconsistent", message, field);

    private static GetReceptionAttentionSummaryResult Failure(GetReceptionAttentionSummaryStatus status, string code, string message, string? field)
    {
        return new(status, null, code, message, field);
    }
}
