using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal sealed class VisitRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public ClientRecord? Client { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset RecordedAt { get; set; }

    public Guid RecordedByAccountId { get; set; }

    public Guid SessionId { get; set; }

    public required string VisitKind { get; set; }

    public required string EntryOrigin { get; set; }

    public Guid? EntryBatchId { get; set; }

    public string? Comment { get; set; }

    public required string Status { get; set; }
}
