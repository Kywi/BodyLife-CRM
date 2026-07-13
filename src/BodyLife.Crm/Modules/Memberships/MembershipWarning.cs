namespace BodyLife.Crm.Modules.Memberships;

public sealed record MembershipWarning
{
    internal MembershipWarning(
        string code,
        MembershipWarningSeverity severity,
        string message)
    {
        Code = code;
        Severity = severity;
        Message = message;
    }

    public string Code { get; }

    public MembershipWarningSeverity Severity { get; }

    public string Message { get; }
}
