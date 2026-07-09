using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public sealed class BusinessAuditAppender(BodyLifeDbContext dbContext)
{
    private static readonly JsonSerializerOptions SummaryJsonOptions = new(JsonSerializerDefaults.Web);

    public AuditEntryId Append(
        CommandEnvelope envelope,
        string actionType,
        string entityType,
        Guid entityId,
        DateTimeOffset recordedAt,
        object? relatedEntityRefs = null,
        object? beforeSummary = null,
        object? afterSummary = null,
        bool changedAfterClose = false)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Audit action type is required.", nameof(actionType));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("Audit entity type is required.", nameof(entityType));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("Audit entity id is required.", nameof(entityId));
        }

        var auditEntryId = AuditEntryId.New();
        var actor = envelope.Actor;
        var entry = new BusinessAuditEntryRecord
        {
            Id = auditEntryId.Value,
            ActionType = actionType.Trim(),
            EntityType = entityType.Trim(),
            EntityId = entityId,
            RelatedEntityRefsJson = SerializeSummary(relatedEntityRefs),
            ActorAccountId = actor.AccountId.Value,
            ActorAccountType = MapAccountKind(actor.AccountKind),
            ActorRole = MapActorRole(actor.Role),
            SessionId = actor.SessionId.Value,
            DeviceLabel = NormalizeOptional(actor.DeviceLabel),
            OccurredAt = envelope.OccurredAt ?? recordedAt,
            RecordedAt = recordedAt,
            Reason = NormalizeOptional(envelope.Reason),
            Comment = NormalizeOptional(envelope.Comment),
            BeforeSummaryJson = SerializeSummary(beforeSummary),
            AfterSummaryJson = SerializeSummary(afterSummary),
            RequestCorrelationId = envelope.RequestCorrelationId.Value.Trim(),
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            IdempotencyKey = NormalizeOptional(envelope.IdempotencyKey),
            ChangedAfterClose = changedAfterClose,
        };

        dbContext.Set<BusinessAuditEntryRecord>().Add(entry);
        return auditEntryId;
    }

    private static string SerializeSummary(object? value)
    {
        return value is null
            ? "{}"
            : JsonSerializer.Serialize(value, SummaryJsonOptions);
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapActorRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static string MapEntryOrigin(EntryOrigin entryOrigin)
    {
        return entryOrigin switch
        {
            EntryOrigin.Normal => "normal",
            EntryOrigin.ManualBackfill => "manual_backfill",
            EntryOrigin.PaperFallback => "paper_fallback",
            EntryOrigin.FutureImport => "future_import",
            _ => throw new ArgumentOutOfRangeException(nameof(entryOrigin), entryOrigin, null),
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
