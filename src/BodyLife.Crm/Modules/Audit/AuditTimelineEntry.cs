using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Audit;

public sealed record AuditTimelineEntry(
    AuditEntryId AuditEntryId,
    string ActionType,
    AuditTimelineEntityType EntityType,
    Guid EntityId,
    AccountId ActorAccountId,
    AccountKind ActorAccountKind,
    ActorRole ActorRole,
    SessionId SessionId,
    string? DeviceLabel,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    string? Reason,
    string? Comment,
    string RelatedEntityRefsJson,
    string BeforeSummaryJson,
    string AfterSummaryJson,
    RequestCorrelationId RequestCorrelationId,
    string? IdempotencyKey,
    bool ChangedAfterClose);
