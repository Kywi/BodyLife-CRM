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

        if (recordedAt == default)
        {
            throw new ArgumentException("Audit recorded time is required.", nameof(recordedAt));
        }

        var actor = envelope.Actor
            ?? throw new ArgumentException("Audit actor is required.", nameof(envelope));
        if (actor.AccountId.Value == Guid.Empty)
        {
            throw new ArgumentException("Audit actor account id is required.", nameof(envelope));
        }

        if (actor.SessionId.Value == Guid.Empty)
        {
            throw new ArgumentException("Audit actor session id is required.", nameof(envelope));
        }

        var normalizedActionType = actionType.Trim();
        var normalizedEntityType = entityType.Trim();
        var requestCorrelationId = envelope.RequestCorrelationId.Value?.Trim();
        if (string.IsNullOrWhiteSpace(requestCorrelationId))
        {
            throw new ArgumentException(
                "Audit request correlation id is required.",
                nameof(envelope));
        }

        var actorAccountType = MapAccountKind(actor.AccountKind);
        var actorRole = MapActorRole(actor.Role);
        var entryOrigin = MapEntryOrigin(envelope.EntryOrigin);
        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);
        var idempotencyKey = NormalizeOptional(envelope.IdempotencyKey);
        if (envelope.EntryOrigin != EntryOrigin.Normal)
        {
            if (envelope.OccurredAt is null)
            {
                throw new ArgumentException(
                    "Backdated audit entries require an occurred time.",
                    nameof(envelope));
            }

            if (reason is null && comment is null)
            {
                throw new ArgumentException(
                    "Backdated audit entries require a reason or comment.",
                    nameof(envelope));
            }
        }

        var relatedEntityRefsJson = SerializeSummary(relatedEntityRefs);
        var beforeSummaryJson = SerializeSummary(beforeSummary);
        var afterSummaryJson = SerializeSummary(afterSummary);
        BusinessAuditEventMatrix.Validate(
            normalizedActionType,
            normalizedEntityType,
            relatedEntityRefsJson,
            beforeSummaryJson,
            afterSummaryJson,
            reason,
            comment,
            idempotencyKey);

        var auditEntryId = AuditEntryId.New();
        var entry = new BusinessAuditEntryRecord
        {
            Id = auditEntryId.Value,
            ActionType = normalizedActionType,
            EntityType = normalizedEntityType,
            EntityId = entityId,
            RelatedEntityRefsJson = relatedEntityRefsJson,
            ActorAccountId = actor.AccountId.Value,
            ActorAccountType = actorAccountType,
            ActorRole = actorRole,
            SessionId = actor.SessionId.Value,
            DeviceLabel = NormalizeOptional(actor.DeviceLabel),
            OccurredAt = envelope.OccurredAt ?? recordedAt,
            RecordedAt = recordedAt,
            Reason = reason,
            Comment = comment,
            BeforeSummaryJson = beforeSummaryJson,
            AfterSummaryJson = afterSummaryJson,
            RequestCorrelationId = requestCorrelationId,
            EntryOrigin = entryOrigin,
            IdempotencyKey = idempotencyKey,
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
