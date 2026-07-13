namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed class IssuedMembershipRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public Guid MembershipTypeId { get; set; }

    public required string TypeNameSnapshot { get; set; }

    public int DurationDaysSnapshot { get; set; }

    public int VisitsLimitSnapshot { get; set; }

    public decimal PriceAmountSnapshot { get; set; }

    public required string PriceCurrencySnapshot { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly BaseEndDate { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public Guid IssuedByAccountId { get; set; }

    public required string Status { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public string? Comment { get; set; }
}
