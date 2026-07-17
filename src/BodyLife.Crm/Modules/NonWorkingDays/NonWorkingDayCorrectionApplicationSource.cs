using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionApplicationSource
{
    public NonWorkingDayCorrectionApplicationSource(
        Guid applicationId,
        Guid membershipId,
        Guid clientId,
        DateRange appliedRange,
        DateTimeOffset previewedAt,
        DateTimeOffset confirmedAt,
        NonWorkingDayCorrectionSourceStatus status)
    {
        RequireId(applicationId, nameof(applicationId));
        RequireId(membershipId, nameof(membershipId));
        RequireId(clientId, nameof(clientId));

        if (previewedAt > confirmedAt)
        {
            throw new ArgumentException(
                "NonWorkingDay application preview cannot follow confirmation.",
                nameof(previewedAt));
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "NonWorkingDay application source status is not supported.");
        }

        ApplicationId = applicationId;
        MembershipId = membershipId;
        ClientId = clientId;
        AppliedRange = appliedRange;
        PreviewedAt = previewedAt;
        ConfirmedAt = confirmedAt;
        Status = status;
    }

    public Guid ApplicationId { get; }

    public Guid MembershipId { get; }

    public Guid ClientId { get; }

    public DateRange AppliedRange { get; }

    public DateTimeOffset PreviewedAt { get; }

    public DateTimeOffset ConfirmedAt { get; }

    public NonWorkingDayCorrectionSourceStatus Status { get; }

    private static void RequireId(Guid id, string parameterName)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A non-empty id is required.", parameterName);
        }
    }
}
