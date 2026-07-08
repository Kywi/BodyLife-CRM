using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Application.Commands;

public sealed record CommandEnvelope(
    ActorContext Actor,
    RequestCorrelationId RequestCorrelationId,
    EntryOrigin EntryOrigin,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey,
    string? Reason,
    string? Comment);
