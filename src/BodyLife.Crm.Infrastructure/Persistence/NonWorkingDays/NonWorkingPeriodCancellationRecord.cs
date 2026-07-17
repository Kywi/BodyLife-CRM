namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodCancellationRecord
{
    public Guid Id { get; set; }

    public Guid NonWorkingPeriodId { get; set; }

    public NonWorkingPeriodRecord? NonWorkingPeriod { get; set; }

    public required string Reason { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }
}
