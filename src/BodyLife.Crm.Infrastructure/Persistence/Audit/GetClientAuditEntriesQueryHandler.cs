using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

public sealed class GetClientAuditEntriesQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<GetClientAuditEntriesQuery, GetClientAuditEntriesResult>
{
    public async Task<GetClientAuditEntriesResult> ExecuteAsync(
        GetClientAuditEntriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await ClientQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return GetClientAuditEntriesResult.Denied();
        }

        var validationFailure = ValidateAndNormalize(query, out var normalized);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var request = normalized!;

        if (!await dbContext.Set<ClientRecord>()
                .AsNoTracking()
                .AnyAsync(
                    client => client.Id == request.ClientId,
                    cancellationToken))
        {
            return GetClientAuditEntriesResult.MissingClient();
        }

        var entries = BusinessAuditQuerySupport.WhereLinkedToClient(
            dbContext.Set<BusinessAuditEntryRecord>().AsNoTracking(),
            request.ClientId);

        if (request.OccurredFromInclusive is { } occurredFromInclusive)
        {
            entries = entries.Where(entry => entry.OccurredAt >= occurredFromInclusive);
        }

        if (request.OccurredBeforeExclusive is { } occurredBeforeExclusive)
        {
            entries = entries.Where(entry => entry.OccurredAt < occurredBeforeExclusive);
        }

        if (request.EntityTypeNames.Count > 0)
        {
            entries = entries.Where(entry => request.EntityTypeNames.Contains(entry.EntityType));
        }

        if (request.ActionTypes.Count > 0)
        {
            entries = entries.Where(entry => request.ActionTypes.Contains(entry.ActionType));
        }

        if (request.AuditEntryIds.Count > 0)
        {
            entries = entries.Where(entry => request.AuditEntryIds.Contains(entry.Id));
        }

        var storedRows = await entries
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.RecordedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(request.Offset)
            .Take(request.Limit + 1)
            .Select(entry => new ClientAuditStorageRow(
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

        if (request.AuditEntryIds.Count > 0
            && (hasMore || storedRows.Length != request.AuditEntryIds.Count))
        {
            return GetClientAuditEntriesResult.InconsistentSource();
        }

        try
        {
            var items = storedRows
                .Take(request.Limit)
                .Select(Map)
                .ToArray();
            var page = ClientAuditEntriesPage.Create(
                request.ClientId,
                request.OccurredFromInclusive,
                request.OccurredBeforeExclusive,
                request.EntityFilters,
                request.ActionTypes,
                request.Offset,
                items,
                hasMore);
            return GetClientAuditEntriesResult.Succeeded(page);
        }
        catch (ArgumentException)
        {
            return GetClientAuditEntriesResult.InconsistentSource();
        }
        catch (InvalidOperationException)
        {
            return GetClientAuditEntriesResult.InconsistentSource();
        }
    }

    private static GetClientAuditEntriesResult? ValidateAndNormalize(
        GetClientAuditEntriesQuery query,
        out NormalizedClientAuditQuery? normalized)
    {
        normalized = null;
        if (query.ClientId == Guid.Empty)
        {
            return GetClientAuditEntriesResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.Limit is < 1 or > GetClientAuditEntriesQuery.MaxLimit)
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Limit must be between 1 and {GetClientAuditEntriesQuery.MaxLimit}.",
                "limit");
        }

        if (query.Offset is < 0 or > GetClientAuditEntriesQuery.MaxOffset)
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Offset must be between 0 and {GetClientAuditEntriesQuery.MaxOffset}.",
                "offset");
        }

        var occurredFromInclusive = query.OccurredFromInclusive?.ToUniversalTime();
        var occurredBeforeExclusive = query.OccurredBeforeExclusive?.ToUniversalTime();
        if (occurredFromInclusive is not null
            && occurredBeforeExclusive is not null
            && occurredFromInclusive >= occurredBeforeExclusive)
        {
            return GetClientAuditEntriesResult.Invalid(
                "Occurred-from time must be earlier than occurred-before time.",
                "occurredBeforeExclusive");
        }

        var entityFilters = query.EntityFilters?.ToArray() ?? [];
        if (entityFilters.Any(filter => !Enum.IsDefined(filter)))
        {
            return GetClientAuditEntriesResult.Invalid(
                "Entity filter is invalid.",
                "entityFilters");
        }

        entityFilters = entityFilters.Distinct().ToArray();
        var actionTypes = query.ActionTypes?.ToArray() ?? [];
        if (actionTypes.Length > GetClientAuditEntriesQuery.MaxActionTypeCount)
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Action types cannot contain more than {GetClientAuditEntriesQuery.MaxActionTypeCount} items.",
                "actionTypes");
        }

        if (actionTypes.Any(string.IsNullOrWhiteSpace))
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Action types must be non-blank and at most {GetClientAuditEntriesQuery.MaxActionTypeLength} characters.",
                "actionTypes");
        }

        actionTypes = actionTypes
            .Select(actionType => actionType.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (actionTypes.Any(actionType =>
                actionType.Length > GetClientAuditEntriesQuery.MaxActionTypeLength))
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Action types must be non-blank and at most {GetClientAuditEntriesQuery.MaxActionTypeLength} characters.",
                "actionTypes");
        }

        var auditEntryIds = query.AuditEntryIds?.ToArray() ?? [];
        if (auditEntryIds.Length > GetClientAuditEntriesQuery.MaxAuditEntryIdCount
            || auditEntryIds.Any(auditEntryId => auditEntryId.Value == Guid.Empty))
        {
            return GetClientAuditEntriesResult.Invalid(
                $"Audit entry ids must be non-empty and contain at most {GetClientAuditEntriesQuery.MaxAuditEntryIdCount} items.",
                "auditEntryIds");
        }

        auditEntryIds = auditEntryIds.Distinct().ToArray();
        if (auditEntryIds.Length > 0
            && (query.Offset != 0 || query.Limit < auditEntryIds.Length))
        {
            return GetClientAuditEntriesResult.Invalid(
                "Exact audit entry selection requires offset 0 and a limit covering every selected id.",
                "auditEntryIds");
        }

        normalized = new NormalizedClientAuditQuery(
            query.ClientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            entityFilters,
            entityFilters.Select(ClientAuditEntityTypes.Map).ToArray(),
            actionTypes,
            auditEntryIds.Select(auditEntryId => auditEntryId.Value).ToArray(),
            query.Limit,
            query.Offset);
        return null;
    }

    private static ClientAuditEntry Map(ClientAuditStorageRow row)
    {
        return new ClientAuditEntry(
            new AuditEntryId(row.Id),
            row.ActionType,
            ClientAuditEntityTypes.Map(row.EntityType),
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

    private static class ClientAuditEntityTypes
    {
        internal const string Client = "client";

        internal static string Map(ClientAuditEntityFilter filter)
        {
            return filter switch
            {
                ClientAuditEntityFilter.Client => Client,
                ClientAuditEntityFilter.Membership => "membership",
                ClientAuditEntityFilter.MembershipOpeningState => "membership_opening_state",
                ClientAuditEntityFilter.Visit => "visit",
                ClientAuditEntityFilter.Payment => "payment",
                ClientAuditEntityFilter.Freeze => "freeze",
                ClientAuditEntityFilter.NonWorkingPeriod => "non_working_period",
                ClientAuditEntityFilter.MembershipNegativeClosure
                    => "membership_negative_closure",
                _ => throw new ArgumentOutOfRangeException(nameof(filter), filter, null),
            };
        }

        internal static ClientAuditEntityFilter Map(string entityType)
        {
            return entityType switch
            {
                Client => ClientAuditEntityFilter.Client,
                "membership" => ClientAuditEntityFilter.Membership,
                "membership_opening_state" => ClientAuditEntityFilter.MembershipOpeningState,
                "visit" => ClientAuditEntityFilter.Visit,
                "payment" => ClientAuditEntityFilter.Payment,
                "freeze" => ClientAuditEntityFilter.Freeze,
                "non_working_period" => ClientAuditEntityFilter.NonWorkingPeriod,
                "membership_negative_closure"
                    => ClientAuditEntityFilter.MembershipNegativeClosure,
                _ => throw new InvalidOperationException(
                    $"Unsupported client audit entity type '{entityType}'."),
            };
        }
    }

    private sealed record NormalizedClientAuditQuery(
        Guid ClientId,
        DateTimeOffset? OccurredFromInclusive,
        DateTimeOffset? OccurredBeforeExclusive,
        IReadOnlyList<ClientAuditEntityFilter> EntityFilters,
        IReadOnlyList<string> EntityTypeNames,
        IReadOnlyList<string> ActionTypes,
        IReadOnlyList<Guid> AuditEntryIds,
        int Limit,
        int Offset);

    private sealed record ClientAuditStorageRow(
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
