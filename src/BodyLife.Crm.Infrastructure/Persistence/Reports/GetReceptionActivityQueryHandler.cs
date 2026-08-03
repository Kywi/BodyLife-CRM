using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace BodyLife.Crm.Infrastructure.Persistence.Reports;

public sealed class GetReceptionActivityQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider,
    IReceptionActivityCursorProtector cursorCodec,
    IBodyLifeQueryHandler<GetClientMembershipStatesQuery, GetClientMembershipStatesResult> membershipStates)
    : IBodyLifeQueryHandler<GetReceptionActivityQuery, GetReceptionActivityResult>
{
    private static readonly HashSet<string> AllowedActions = new(StringComparer.Ordinal)
    {
        ClientAuditActions.Created, ClientAuditActions.Updated, ClientAuditActions.CardAssigned,
        ClientAuditActions.CardChanged, ClientAuditActions.CardCleared, MembershipAuditActions.Issued,
        MembershipAuditActions.Replaced, MembershipAuditActions.SaleCanceled,
        MembershipAuditActions.OpeningStateCreated, VisitAuditActions.Marked, VisitAuditActions.Canceled,
        PaymentAuditActions.Created, PaymentAuditActions.Corrected, PaymentAuditActions.Canceled,
        FreezeAuditActions.Added, FreezeAuditActions.Canceled,
    };

    public async Task<GetReceptionActivityResult> ExecuteAsync(GetReceptionActivityQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await MembershipQuerySupport.IsActorAuthorizedAsync(dbContext, query.Actor, timeProvider.GetUtcNow(), cancellationToken))
            return GetReceptionActivityResult.PermissionDenied();
        if (query.RecordedBusinessDate == default || query.RecordedBusinessDate == DateOnly.MaxValue)
            return GetReceptionActivityResult.Invalid("Recorded business date is required.", "recordedBusinessDate");
        if (query.Limit is < 1 or > GetReceptionActivityQuery.MaxLimit)
            return GetReceptionActivityResult.Invalid("Limit must be between 1 and 20.", "limit");

        if (!cursorCodec.TryDecode(query.Cursor, query.RecordedBusinessDate, out var cursor))
            return GetReceptionActivityResult.Invalid("Cursor is invalid for the requested recorded business date.", "cursor");

        var range = BusinessTimeZone.GetUtcDayRange(query.RecordedBusinessDate);
        var audit = dbContext.Set<BusinessAuditEntryRecord>().AsNoTracking()
            .Where(entry => entry.RecordedAt >= range.FromInclusive && entry.RecordedAt < range.ToExclusive && AllowedActions.Contains(entry.ActionType));
        if (cursor is not null)
        {
            audit = audit.Where(entry => EF.Functions.LessThan(
                ValueTuple.Create(entry.RecordedAt, entry.Id),
                ValueTuple.Create(cursor.RecordedAt, cursor.AuditId)));
        }

        var entries = await audit.OrderByDescending(entry => entry.RecordedAt).ThenByDescending(entry => entry.Id)
            .Take(query.Limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = entries.Length > query.Limit;
        var parsed = new List<ParsedEntry>(query.Limit);
        foreach (var entry in entries.Take(query.Limit))
        {
            if (!TryParse(entry, out var parsedEntry)) return GetReceptionActivityResult.SourceInconsistent();
            parsed.Add(parsedEntry);
        }

        var clientIds = parsed.Select(entry => entry.ClientId).Distinct().ToArray();
        var clients = await dbContext.Set<ClientRecord>().AsNoTracking().Where(client => clientIds.Contains(client.Id))
            .ToDictionaryAsync(client => client.Id, cancellationToken);
        if (clients.Count != clientIds.Length || clients.Values.Any(client => string.IsNullOrWhiteSpace(DisplayName(client))))
            return GetReceptionActivityResult.SourceInconsistent();

        var states = new Dictionary<Guid, ReceptionActivityMembershipState>();
        foreach (var clientId in clientIds)
        {
            var stateResult = await membershipStates.ExecuteAsync(
                new GetClientMembershipStatesQuery(query.Actor, clientId, query.RecordedBusinessDate), cancellationToken);
            if (stateResult.Status == GetClientMembershipStatesStatus.RecalculationFailed) return GetReceptionActivityResult.RecalculationFailed();
            if (stateResult.Status != GetClientMembershipStatesStatus.Success || stateResult.StateCollection is null)
                return GetReceptionActivityResult.SourceInconsistent();
            try { states[clientId] = CreateCompactState(stateResult.StateCollection); }
            catch (ArgumentException) { return GetReceptionActivityResult.SourceInconsistent(); }
        }

        try
        {
            var rows = parsed.Select(entry => new ReceptionActivityItem(
                entry.EventType, entry.Entry.Id, entry.Entry.EntityId, entry.ClientId,
                DisplayName(clients[entry.ClientId]), entry.RelatedEntities,
                entry.Entry.OccurredAt, entry.Entry.RecordedAt, entry.EntryOrigin,
                entry.IsCorrectionOrCancellation, entry.Entry.ChangedAfterClose, states[entry.ClientId])).ToArray();
            var next = hasMore ? cursorCodec.Encode(query.RecordedBusinessDate, rows[^1].RecordedAt, rows[^1].AuditEntryId) : null;
            return GetReceptionActivityResult.Succeeded(ReceptionActivityPage.Create(rows, next, hasMore));
        }
        catch (ArgumentException) { return GetReceptionActivityResult.SourceInconsistent(); }
    }

    private static ReceptionActivityMembershipState CreateCompactState(ClientMembershipStatesReadModel states)
    {
        var selection = states.ActiveCandidateSelection;
        var candidates = selection.Candidates.Select(candidate => CreateCandidate(candidate.State)).ToArray();
        return selection.Status switch
        {
            ActiveMembershipCandidateStatus.Single => ReceptionActivityMembershipState.Create(
                ReceptionActivityMembershipSelectionStatus.Single,
                candidates.Length == 1 ? candidates[0] : throw new ArgumentException("Single membership selection has an invalid candidate count."),
                candidates),
            ActiveMembershipCandidateStatus.Ambiguous => ReceptionActivityMembershipState.Create(
                ReceptionActivityMembershipSelectionStatus.Ambiguous, null, candidates),
            ActiveMembershipCandidateStatus.None => ReceptionActivityMembershipState.Create(
                ReceptionActivityMembershipSelectionStatus.None,
                states.Timeline.FirstOrDefault() is { } latest ? CreateCandidate(latest.State) : null,
                []),
            _ => throw new ArgumentException("Membership candidate selection is unsupported."),
        };
    }

    private static ReceptionActivityMembershipCandidate CreateCandidate(MembershipStateReadModel state) => new(
        state.MembershipId, state.RemainingVisits, state.NegativeBalance, state.EffectiveEndDate,
        Array.AsReadOnly(state.Warnings.ToArray()));

    private static bool TryParse(BusinessAuditEntryRecord entry, out ParsedEntry parsed)
    {
        parsed = default!;
        if (!TryMapAction(entry.ActionType, entry.EntityType, out var type, out var correction)
            || entry.Id == Guid.Empty || entry.EntityId == Guid.Empty
            || !BusinessTimeZone.TryNormalizeUtcInstant(entry.OccurredAt, out _)
            || !BusinessTimeZone.TryNormalizeUtcInstant(entry.RecordedAt, out _)
            || !TryMapEntryOrigin(entry.EntryOrigin, out var origin)
            || !TryReadRelated(entry, out var clientId, out var relatedEntities))
            return false;
        parsed = new ParsedEntry(entry, type, clientId, Array.AsReadOnly(relatedEntities.ToArray()), origin, correction);
        return true;
    }

    private static bool TryReadRelated(BusinessAuditEntryRecord entry, out Guid clientId, out List<ReceptionActivityRelatedEntity> related)
    {
        clientId = entry.EntityType == ClientAuditActions.EntityType ? entry.EntityId : Guid.Empty;
        related = [];
        try
        {
            using var document = JsonDocument.Parse(entry.RelatedEntityRefsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var json = document.RootElement;
            if (clientId == Guid.Empty && !TryReadGuid(json, "clientId", required: true, out clientId)) return false;
            if (clientId == Guid.Empty) return false;
            return entry.ActionType switch
            {
                var action when action is ClientAuditActions.Created or ClientAuditActions.Updated => true,
                var action when action is ClientAuditActions.CardAssigned or ClientAuditActions.CardChanged or ClientAuditActions.CardCleared
                    => TryAddOptional(json, "currentCardAssignmentId", ReceptionActivityRelatedEntityType.CardAssignment, related)
                        && TryAddOptional(json, "previousCardAssignmentId", ReceptionActivityRelatedEntityType.CardAssignment, related),
                var action when action == MembershipAuditActions.Issued
                    => TryAddOptional(json, "paymentId", ReceptionActivityRelatedEntityType.Payment, related),
                var action when action == MembershipAuditActions.Replaced
                    => TryReadIssuedSaleCorrectionRelated(
                        entry,
                        json,
                        isCancellation: false,
                        related),
                var action when action == MembershipAuditActions.SaleCanceled
                    => TryReadIssuedSaleCorrectionRelated(
                        entry,
                        json,
                        isCancellation: true,
                        related),
                var action when action == MembershipAuditActions.OpeningStateCreated
                    => TryAddRequired(json, "membershipId", ReceptionActivityRelatedEntityType.Membership, related),
                var action when action is VisitAuditActions.Marked or VisitAuditActions.Canceled
                    => TryAddOptional(json, "membershipId", ReceptionActivityRelatedEntityType.Membership, related),
                var action when action is PaymentAuditActions.Created or PaymentAuditActions.Corrected or PaymentAuditActions.Canceled
                    => TryAddOptional(json, "membershipId", ReceptionActivityRelatedEntityType.Membership, related),
                var action when action is FreezeAuditActions.Added or FreezeAuditActions.Canceled
                    => TryAddRequired(json, "membershipId", ReceptionActivityRelatedEntityType.Membership, related),
                _ => false,
            };
        }
        catch (JsonException) { return false; }
    }

    private static bool TryAddRequired(JsonElement json, string property, ReceptionActivityRelatedEntityType type, List<ReceptionActivityRelatedEntity> related)
    {
        if (!TryReadGuid(json, property, required: true, out var id)) return false;
        related.Add(new ReceptionActivityRelatedEntity(type, id));
        return true;
    }

    private static bool TryReadIssuedSaleCorrectionRelated(
        BusinessAuditEntryRecord entry,
        JsonElement json,
        bool isCancellation,
        List<ReceptionActivityRelatedEntity> related)
    {
        if (!TryReadGuid(json, "saleCorrectionId", required: true, out _)
            || !TryReadGuid(json, "originalMembershipId", required: true, out var originalMembershipId)
            || originalMembershipId != entry.EntityId
            || !TryReadGuid(json, "paymentLifecycleAuditEntryId", required: true, out _)
            || !TryReadGuid(
                json,
                "originalPaymentId",
                required: true,
                out var originalPaymentId))
        {
            return false;
        }

        related.Add(new ReceptionActivityRelatedEntity(
            ReceptionActivityRelatedEntityType.Payment,
            originalPaymentId));

        if (isCancellation)
        {
            return IsExplicitNull(json, "replacementMembershipId")
                && IsExplicitNull(json, "replacementPaymentId")
                && IsExplicitNull(json, "replacementPaymentCreatedAuditEntryId");
        }

        if (!TryReadGuid(
                json,
                "replacementMembershipId",
                required: true,
                out var replacementMembershipId)
            || replacementMembershipId == originalMembershipId
            || !TryReadGuid(
                json,
                "replacementPaymentId",
                required: true,
                out var replacementPaymentId)
            || replacementPaymentId == originalPaymentId
            || !TryReadGuid(
                json,
                "replacementPaymentCreatedAuditEntryId",
                required: true,
                out _))
        {
            return false;
        }

        related.Add(new ReceptionActivityRelatedEntity(
            ReceptionActivityRelatedEntityType.Membership,
            replacementMembershipId));
        related.Add(new ReceptionActivityRelatedEntity(
            ReceptionActivityRelatedEntityType.Payment,
            replacementPaymentId));
        return true;
    }

    private static bool IsExplicitNull(JsonElement json, string property)
    {
        return json.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.Null;
    }

    private static bool TryAddOptional(JsonElement json, string property, ReceptionActivityRelatedEntityType type, List<ReceptionActivityRelatedEntity> related)
    {
        if (!TryReadGuid(json, property, required: false, out var id)) return false;
        if (id != Guid.Empty) related.Add(new ReceptionActivityRelatedEntity(type, id));
        return true;
    }

    private static bool TryReadGuid(JsonElement json, string property, bool required, out Guid id)
    {
        id = Guid.Empty;
        if (!json.TryGetProperty(property, out var value)) return !required;
        if (value.ValueKind == JsonValueKind.Null) return !required;
        return value.ValueKind == JsonValueKind.String && Guid.TryParseExact(value.GetString(), "D", out id) && id != Guid.Empty;
    }

    private static bool TryMapAction(string action, string entity, out ReceptionActivityEventType type, out bool correction)
    {
        correction = false;
        type = action switch
        {
            "client.created" when entity == "client" => ReceptionActivityEventType.ClientCreated,
            "client.updated" when entity == "client" => ReceptionActivityEventType.ClientUpdated,
            "card.assigned" when entity == "client" => ReceptionActivityEventType.CardAssigned,
            "card.changed" when entity == "client" => ReceptionActivityEventType.CardChanged,
            "card.cleared" when entity == "client" => ReceptionActivityEventType.CardCleared,
            "membership.issued" when entity == "membership" => ReceptionActivityEventType.MembershipIssued,
            "membership.replaced" when entity == "membership" => ReceptionActivityEventType.MembershipReplaced,
            "membership.sale_canceled" when entity == "membership" => ReceptionActivityEventType.MembershipSaleCanceled,
            "membership_opening_state.created" when entity == "membership_opening_state" => ReceptionActivityEventType.MembershipOpeningStateCreated,
            "visit.marked" when entity == "visit" => ReceptionActivityEventType.VisitMarked,
            "visit.canceled" when entity == "visit" => ReceptionActivityEventType.VisitCanceled,
            "payment.created" when entity == "payment" => ReceptionActivityEventType.PaymentCreated,
            "payment.corrected" when entity == "payment" => ReceptionActivityEventType.PaymentCorrected,
            "payment.canceled" when entity == "payment" => ReceptionActivityEventType.PaymentCanceled,
            "freeze.added" when entity == "freeze" => ReceptionActivityEventType.FreezeAdded,
            "freeze.canceled" when entity == "freeze" => ReceptionActivityEventType.FreezeCanceled,
            _ => default,
        };
        correction = type is ReceptionActivityEventType.CardChanged or ReceptionActivityEventType.CardCleared
            or ReceptionActivityEventType.VisitCanceled or ReceptionActivityEventType.PaymentCorrected
            or ReceptionActivityEventType.PaymentCanceled or ReceptionActivityEventType.FreezeCanceled
            or ReceptionActivityEventType.MembershipReplaced
            or ReceptionActivityEventType.MembershipSaleCanceled;
        return Enum.IsDefined(type);
    }

    private static bool TryMapEntryOrigin(string origin, out EntryOrigin mapped)
    {
        mapped = origin switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => default,
        };
        return Enum.IsDefined(mapped);
    }

    private static string DisplayName(ClientRecord client) => string.Join(" ", new[] { client.Surname, client.Name, client.Patronymic }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private sealed record ParsedEntry(BusinessAuditEntryRecord Entry, ReceptionActivityEventType EventType, Guid ClientId, IReadOnlyList<ReceptionActivityRelatedEntity> RelatedEntities, EntryOrigin EntryOrigin, bool IsCorrectionOrCancellation);
}
