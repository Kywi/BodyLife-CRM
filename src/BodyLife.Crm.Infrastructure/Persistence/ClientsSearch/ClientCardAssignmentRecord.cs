namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal sealed class ClientCardAssignmentRecord
{
    public Guid Id { get; set; }

    public Guid ClientId { get; set; }

    public required string CardNumberRaw { get; set; }

    public required string CardNumberNormalized { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public Guid AssignedByAccountId { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public Guid? EndedByAccountId { get; set; }

    public string? EndReason { get; set; }

    public bool IsCurrent { get; set; }
}
