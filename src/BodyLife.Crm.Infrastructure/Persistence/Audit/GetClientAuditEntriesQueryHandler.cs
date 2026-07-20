using System.Text.Json;
using BodyLife.Crm.Application.Commands;
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
    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);

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

        var scalarClientReference = JsonSerializer.Serialize(
            new ScalarClientReference(request.ClientId),
            AuditJsonOptions);
        var affectedClientReference = JsonSerializer.Serialize(
            new AffectedClientReference([request.ClientId]),
            AuditJsonOptions);
        var entries = dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .Where(entry =>
                (entry.EntityType == ClientAuditEntityTypes.Client
                    && entry.EntityId == request.ClientId)
                || EF.Functions.JsonContains(
                    entry.RelatedEntityRefsJson,
                    scalarClientReference)
                || EF.Functions.JsonContains(
                    entry.RelatedEntityRefsJson,
                    affectedClientReference));

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
        normalized = new NormalizedClientAuditQuery(
            query.ClientId,
            occurredFromInclusive,
            occurredBeforeExclusive,
            entityFilters,
            entityFilters.Select(ClientAuditEntityTypes.Map).ToArray(),
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
            MapAccountKind(row.ActorAccountType),
            MapActorRole(row.ActorRole),
            new SessionId(row.SessionId),
            row.DeviceLabel,
            row.OccurredAt,
            row.RecordedAt,
            MapEntryOrigin(row.EntryOrigin),
            row.Reason,
            row.Comment,
            row.RelatedEntityRefsJson,
            row.BeforeSummaryJson,
            row.AfterSummaryJson,
            new RequestCorrelationId(row.RequestCorrelationId),
            row.IdempotencyKey,
            row.ChangedAfterClose);
    }

    private static AccountKind MapAccountKind(string accountType)
    {
        return accountType switch
        {
            "owner" => AccountKind.Owner,
            "named_admin" => AccountKind.NamedAdmin,
            "shared_reception_admin" => AccountKind.SharedReceptionAdmin,
            _ => throw new InvalidOperationException(
                $"Unsupported audit actor account type '{accountType}'."),
        };
    }

    private static ActorRole MapActorRole(string role)
    {
        return role switch
        {
            "owner" => ActorRole.Owner,
            "admin" => ActorRole.Admin,
            _ => throw new InvalidOperationException(
                $"Unsupported audit actor role '{role}'."),
        };
    }

    private static EntryOrigin MapEntryOrigin(string entryOrigin)
    {
        return entryOrigin switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => throw new InvalidOperationException(
                $"Unsupported audit entry origin '{entryOrigin}'."),
        };
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

    private sealed record ScalarClientReference(Guid ClientId);

    private sealed record AffectedClientReference(IReadOnlyList<Guid> AffectedClientIds);

    private sealed record NormalizedClientAuditQuery(
        Guid ClientId,
        DateTimeOffset? OccurredFromInclusive,
        DateTimeOffset? OccurredBeforeExclusive,
        IReadOnlyList<ClientAuditEntityFilter> EntityFilters,
        IReadOnlyList<string> EntityTypeNames,
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
