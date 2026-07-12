namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

internal sealed class MembershipTypeRecord
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public int DurationDays { get; set; }

    public int VisitsLimit { get; set; }

    public decimal PriceAmount { get; set; }

    public required string PriceCurrency { get; set; }

    public bool IsActive { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? DeactivatedAt { get; set; }
}
