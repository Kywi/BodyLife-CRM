using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public sealed class GetAuditTimelineQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetAuditTimelineQuery, GetAuditTimelineResult>
{
    public async Task<GetAuditTimelineResult> ExecuteAsync(
        GetAuditTimelineQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await ClientQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetAuditTimelineResult.Denied();
        }

        var validationFailure = ValidateAndNormalize(query, out var normalized);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var request = normalized!;
        if (request.ClientId is { } clientId
            && !await dbContext.Set<ClientRecord>()
                .AsNoTracking()
                .AnyAsync(client => client.Id == clientId, cancellationToken))
        {
            return GetAuditTimelineResult.MissingClient();
        }

        IQueryable<BusinessAuditEntryRecord> entries = dbContext
            .Set<BusinessAuditEntryRecord>()
            .AsNoTracking();
        if (request.ClientId is { } selectedClientId)
        {
            entries = BusinessAuditQuerySupport.WhereLinkedToClient(
                entries,
                selectedClientId);
        }

        if (request.EntityTypeName is { } entityTypeName)
        {
            entries = entries.Where(entry => entry.EntityType == entityTypeName);
        }

        if (request.EntityId is { } entityId)
        {
            entries = entries.Where(entry => entry.EntityId == entityId);
        }

        if (request.RecordedFromInclusive is { } recordedFromInclusive)
        {
            entries = entries.Where(entry => entry.RecordedAt >= recordedFromInclusive);
        }

        if (request.RecordedBeforeExclusive is { } recordedBeforeExclusive)
        {
            entries = entries.Where(entry => entry.RecordedAt < recordedBeforeExclusive);
        }

        if (request.ActionTypes.Count > 0)
        {
            entries = entries.Where(entry => request.ActionTypes.Contains(entry.ActionType));
        }

        var storedRows = await entries
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(request.Offset)
            .Take(request.Limit + 1)
            .Select(entry => new AuditTimelineStorageRow(
                entry.Id,
                entry.ActionType,
                entry.EntityType,
                entry.EntityId,
                entry.ActorAccountId,
                entry.ActorAccountType,
                entry.ActorRole,
                entry.SessionId,
                entry.DeviceLabel,
                entry.OccurredAt,
                entry.RecordedAt,
                entry.EntryOrigin,
                entry.Reason,
                entry.Comment,
                entry.RelatedEntityRefsJson,
                entry.BeforeSummaryJson,
                entry.AfterSummaryJson,
                entry.RequestCorrelationId,
                entry.IdempotencyKey,
                entry.ChangedAfterClose))
            .ToArrayAsync(cancellationToken);
        var hasMore = storedRows.Length > request.Limit;

        try
        {
            var items = storedRows
                .Take(request.Limit)
                .Select(Map)
                .ToArray();
            return GetAuditTimelineResult.Succeeded(
                AuditTimelinePage.Create(
                    request.ClientId,
                    request.EntityType,
                    request.EntityId,
                    request.RecordedFromInclusive,
                    request.RecordedBeforeExclusive,
                    request.ActionTypes,
                    request.Offset,
                    items,
                    hasMore));
        }
        catch (ArgumentException)
        {
            return GetAuditTimelineResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetAuditTimelineResult.InconsistentSource();
        }
    }

    private static GetAuditTimelineResult? ValidateAndNormalize(
        GetAuditTimelineQuery query,
        out NormalizedAuditTimelineQuery? normalized)
    {
        normalized = null;
        if (query.ClientId == Guid.Empty)
        {
            return GetAuditTimelineResult.Invalid(
                "Client id cannot be empty.",
                "clientId");
        }

        if (query.EntityType is { } entityType && !Enum.IsDefined(entityType))
        {
            return GetAuditTimelineResult.Invalid(
                "Entity type is invalid.",
                "entityType");
        }

        if (query.EntityId == Guid.Empty
            || (query.EntityId is not null && query.EntityType is null))
        {
            return GetAuditTimelineResult.Invalid(
                "Entity id must be non-empty and paired with an entity type.",
                "entityId");
        }

        if (query.Limit is < 1 or > GetAuditTimelineQuery.MaxLimit)
        {
            return GetAuditTimelineResult.Invalid(
                $"Limit must be between 1 and {GetAuditTimelineQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > GetAuditTimelineQuery.MaxOffset)
        {
            return GetAuditTimelineResult.Invalid(
                $"Offset must be between 0 and {GetAuditTimelineQuery.MaxOffset}.",
                "offset");
        }

        var recordedFromInclusive = query.RecordedFromInclusive?.ToUniversalTime();
        var recordedBeforeExclusive = query.RecordedBeforeExclusive?.ToUniversalTime();
        if (recordedFromInclusive is not null
            && recordedBeforeExclusive is not null
            && recordedFromInclusive >= recordedBeforeExclusive)
        {
            return GetAuditTimelineResult.Invalid(
                "Recorded-from time must be earlier than recorded-before time.",
                "recordedBeforeExclusive");
        }

        var actionTypes = query.ActionTypes?.ToArray() ?? [];
        if (actionTypes.Length > GetAuditTimelineQuery.MaxActionTypeCount)
        {
            return GetAuditTimelineResult.Invalid(
                $"Action types cannot contain more than {GetAuditTimelineQuery.MaxActionTypeCount} items.",
                "actionTypes");
        }

        if (actionTypes.Any(string.IsNullOrWhiteSpace))
        {
            return GetAuditTimelineResult.Invalid(
                $"Action types must be non-blank and at most {GetAuditTimelineQuery.MaxActionTypeLength} characters.",
                "actionTypes");
        }

        actionTypes = actionTypes
            .Select(actionType => actionType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (actionTypes.Any(actionType =>
                actionType.Length > GetAuditTimelineQuery.MaxActionTypeLength))
        {
            return GetAuditTimelineResult.Invalid(
                $"Action types must be non-blank and at most {GetAuditTimelineQuery.MaxActionTypeLength} characters.",
                "actionTypes");
        }

        normalized = new NormalizedAuditTimelineQuery(
            query.ClientId,
            query.EntityType,
            query.EntityType is null
                ? null
                : AuditTimelineEntityTypes.Map(query.EntityType.Value),
            query.EntityId,
            recordedFromInclusive,
            recordedBeforeExclusive,
            actionTypes,
            query.Limit,
            query.Offset);
        return null;
    }

    private static AuditTimelineEntry Map(AuditTimelineStorageRow row)
    {
        return new AuditTimelineEntry(
            new AuditEntryId(row.Id),
            row.ActionType,
            AuditTimelineEntityTypes.Map(row.EntityType),
            row.EntityId,
            new AccountId(row.ActorAccountId),
            BusinessAuditQuerySupport.MapAccountKind(row.ActorAccountType),
            BusinessAuditQuerySupport.MapActorRole(row.ActorRole),
            new SessionId(row.SessionId),
            row.DeviceLabel,
            row.OccurredAt,
            row.RecordedAt,
            BusinessAuditQuerySupport.MapEntryOrigin(row.EntryOrigin),
            row.Reason,
            row.Comment,
            row.RelatedEntityRefsJson,
            row.BeforeSummaryJson,
            row.AfterSummaryJson,
            new RequestCorrelationId(row.RequestCorrelationId),
            row.IdempotencyKey,
            row.ChangedAfterClose);
    }

    private static class AuditTimelineEntityTypes
    {
        internal static string Map(AuditTimelineEntityType entityType)
        {
            return entityType switch
            {
                AuditTimelineEntityType.Client => BusinessAuditQuerySupport.ClientEntityType,
                AuditTimelineEntityType.MembershipType => "membership_type",
                AuditTimelineEntityType.Membership => "membership",
                AuditTimelineEntityType.MembershipOpeningState
                    => "membership_opening_state",
                AuditTimelineEntityType.Visit => "visit",
                AuditTimelineEntityType.Payment => "payment",
                AuditTimelineEntityType.Freeze => "freeze",
                AuditTimelineEntityType.NonWorkingPeriod => "non_working_period",
                AuditTimelineEntityType.StaffAccount => "staff_account",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(entityType),
                    entityType,
                    null),
            };
        }

        internal static AuditTimelineEntityType Map(string entityType)
        {
            return entityType switch
            {
                BusinessAuditQuerySupport.ClientEntityType
                    => AuditTimelineEntityType.Client,
                "membership_type" => AuditTimelineEntityType.MembershipType,
                "membership" => AuditTimelineEntityType.Membership,
                "membership_opening_state"
                    => AuditTimelineEntityType.MembershipOpeningState,
                "visit" => AuditTimelineEntityType.Visit,
                "payment" => AuditTimelineEntityType.Payment,
                "freeze" => AuditTimelineEntityType.Freeze,
                "non_working_period" => AuditTimelineEntityType.NonWorkingPeriod,
                "staff_account" => AuditTimelineEntityType.StaffAccount,
                _ => throw new InvalidOperationException(
                    $"Unsupported audit timeline entity type '{entityType}'."),
            };
        }
    }

    private sealed record NormalizedAuditTimelineQuery(
        Guid? ClientId,
        AuditTimelineEntityType? EntityType,
        string? EntityTypeName,
        Guid? EntityId,
        DateTimeOffset? RecordedFromInclusive,
        DateTimeOffset? RecordedBeforeExclusive,
        IReadOnlyList<string> ActionTypes,
        int Limit,
        int Offset);

    private sealed record AuditTimelineStorageRow(
        Guid Id,
        string ActionType,
        string EntityType,
        Guid EntityId,
        Guid ActorAccountId,
        string ActorAccountType,
        string ActorRole,
        Guid SessionId,
        string? DeviceLabel,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        string? Reason,
        string? Comment,
        string RelatedEntityRefsJson,
        string BeforeSummaryJson,
        string AfterSummaryJson,
        string RequestCorrelationId,
        string? IdempotencyKey,
        bool ChangedAfterClose);
}
