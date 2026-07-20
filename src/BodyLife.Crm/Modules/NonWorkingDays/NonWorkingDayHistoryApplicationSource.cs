using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record NonWorkingDayHistoryApplicationSource(
    Guid ApplicationId,
    Guid PeriodId,
    Guid MembershipId,
    Guid ClientId,
    string MembershipTypeNameSnapshot,
    DateRange AppliedRange,
    DateTimeOffset PreviewedAt,
    DateTimeOffset ConfirmedAt,
    NonWorkingDayCorrectionSourceStatus CurrentStatus);
