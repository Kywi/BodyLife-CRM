using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record NonWorkingDayCancellationHistorySource(
    Guid CancellationId,
    Guid PeriodId,
    string Reason,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId);
