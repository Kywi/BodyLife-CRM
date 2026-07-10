namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class DuplicateWarningAcknowledgementRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public required string WarningType { get; set; }

    public Guid MatchedClientId { get; set; }

    public Guid AcknowledgedByAccountId { get; set; }

    public DateTimeOffset AcknowledgedAt { get; set; }

    public required string Reason { get; set; }
}
