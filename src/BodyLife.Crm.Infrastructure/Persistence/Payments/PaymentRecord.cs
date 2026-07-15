using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal sealed class PaymentRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public ClientRecord? Client { get; set; }

    public Guid? MembershipId { get; set; }

    public IssuedMembershipRecord? Membership { get; set; }

    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public required string Method { get; set; }

    public required string PaymentContext { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public string? Comment { get; set; }

    public required string Status { get; set; }
}
