namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal sealed class NonWorkingPeriodRecord
{
    public Guid Id { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public required string ReasonCode { get; set; }

    public string? ReasonComment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string Status { get; set; }
}
