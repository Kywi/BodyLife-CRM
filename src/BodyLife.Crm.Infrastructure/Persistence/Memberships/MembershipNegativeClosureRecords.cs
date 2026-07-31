namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class MembershipNegativeClosureRecord
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string ClosureType { get; set; } = null!;
    public Guid? CoveringMembershipId { get; set; }
    public Guid OldestOpenNegativeVisitId { get; set; }
    public int VisitsCount { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid RecordedByAccountId { get; set; }
    public Guid SessionId { get; set; }
    public string EntryOrigin { get; set; } = null!;
    public Guid? EntryBatchId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
    public string Status { get; set; } = null!;
}

internal sealed class MembershipNegativeClosureLineRecord
{
    public Guid Id { get; set; }
    public Guid NegativeClosureId { get; set; }
    public Guid MembershipTypeId { get; set; }
    public string TypeNameSnapshot { get; set; } = null!;
    public int DurationDaysSnapshot { get; set; }
    public int VisitsLimitSnapshot { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceAmountSnapshot { get; set; }
    public string CurrencySnapshot { get; set; } = null!;
    public decimal LineTotal { get; set; }
    public int Sequence { get; set; }
}

internal sealed class MembershipNegativeClosureItemRecord
{
    public Guid Id { get; set; }
    public Guid NegativeClosureId { get; set; }
    public Guid ClientId { get; set; }
    public Guid? ClosureLineId { get; set; }
    public int Sequence { get; set; }
    public Guid VisitId { get; set; }
    public Guid SourceMembershipId { get; set; }
    public Guid OldConsumptionId { get; set; }
    public Guid? CoveringMembershipId { get; set; }
    public Guid? NewConsumptionId { get; set; }
    public string Status { get; set; } = null!;
}

internal sealed class MembershipNegativeClosureCorrectionRecord
{
    public Guid Id { get; set; }
    public Guid OriginalClosureId { get; set; }
    public Guid? ReplacementClosureId { get; set; }
    public string Mode { get; set; } = null!;
    public string Reason { get; set; } = null!;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public Guid RecordedByAccountId { get; set; }
    public Guid SessionId { get; set; }
    public string EntryOrigin { get; set; } = null!;
    public Guid? EntryBatchId { get; set; }
    public string IdempotencyKey { get; set; } = null!;
}
