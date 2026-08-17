using System.Globalization;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed record AuditEntryExplanationViewModel(
    string Kind,
    string Title,
    string Narrative,
    string BeforeLabel,
    string AfterLabel,
    IReadOnlyList<AuditEntryExplanationFactViewModel> BeforeFacts,
    IReadOnlyList<AuditEntryExplanationFactViewModel> AfterFacts,
    string? ChangedFields,
    bool IsAvailable);

public sealed class AuditEntryExplanationPresenter(
    AuditPresentation presentation,
    ClientAuditExplanationFactory clientFactory,
    StaffAccountAuditExplanationFactory staffAccountFactory,
    NonWorkingDayAuditExplanationFactory nonWorkingDayFactory)
{
    public AuditPresentation Presentation { get; } = presentation;

    private static readonly IReadOnlyDictionary<string, string> KindsByAction =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["membership_type.created"] = "membership-type-created",
            ["membership_type.edited"] = "membership-type-edited",
            ["membership_type.deactivated"] = "membership-type-deactivated",
            ["membership.issued"] = "membership-issued",
            ["membership.replaced"] = "membership-sale-replaced",
            ["membership.sale_canceled"] = "membership-sale-canceled",
            ["membership_opening_state.created"] = "membership-opening-state-created",
            ["membership_negative_closure.created"] = "membership-negative-closure-created",
            ["membership_negative_closure.canceled"] = "membership-negative-closure-canceled",
            ["membership_negative_closure.replaced"] = "membership-negative-closure-replaced",
            ["client.created"] = "client-created",
            ["client.updated"] = "client-updated",
            ["card.assigned"] = "card-assigned",
            ["card.changed"] = "card-changed",
            ["card.cleared"] = "card-cleared",
            ["staff_account.created"] = "staff-account-created",
            ["staff_account.display_name_updated"] =
                "staff-account-display-name-updated",
            ["staff_account.activated"] = "staff-account-activated",
            ["staff_account.deactivated"] = "staff-account-deactivated",
            ["staff_credentials.configured"] = "staff-credentials-configured",
            ["staff_credentials.reset"] = "staff-credentials-reset",
            ["non_working_day.added"] = "non-working-day-added",
            ["non_working_day.corrected"] = "non-working-day-corrected",
            ["non_working_day.canceled"] = "non-working-day-canceled",
            ["freeze.added"] = "freeze-added",
            ["freeze.canceled"] = "freeze-canceled",
            ["visit.marked"] = "visit-marked",
            ["visit.canceled"] = "visit-canceled",
            ["payment.created"] = "payment-created",
            ["payment.corrected"] = "payment-corrected",
            ["payment.canceled"] = "payment-canceled",
            ["paper_fallback.batch_created"] = "paper-fallback-batch-created",
            ["paper_fallback.row_created"] = "paper-fallback-row-created",
        };

    public static IEnumerable<string> ReadableActionTypes => KindsByAction.Keys;

    public AuditEntryExplanationViewModel? Create(AuditTimelineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!KindsByAction.TryGetValue(entry.ActionType, out var kind))
        {
            return null;
        }

        try
        {
            using var related = JsonDocument.Parse(entry.RelatedEntityRefsJson);
            using var before = JsonDocument.Parse(entry.BeforeSummaryJson);
            using var after = JsonDocument.Parse(entry.AfterSummaryJson);
            if (related.RootElement.ValueKind != JsonValueKind.Object
                || before.RootElement.ValueKind != JsonValueKind.Object
                || after.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    "Audit explanation summaries must be JSON objects.");
            }

            return entry.ActionType switch
            {
                "paper_fallback.batch_created"
                    when entry.EntityType == AuditTimelineEntityType.EntryBatch
                    => CreatePaperFallbackBatchCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "paper_fallback.row_created"
                    when entry.EntityType == AuditTimelineEntityType.EntryBatchRow
                    => CreatePaperFallbackRowCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "membership_type.created"
                    when entry.EntityType == AuditTimelineEntityType.MembershipType
                    => CreateMembershipTypeCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "client.created" when entry.EntityType == AuditTimelineEntityType.Client
                    => clientFactory.CreateClientCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "client.updated" when entry.EntityType == AuditTimelineEntityType.Client
                    => clientFactory.CreateClientUpdate(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.assigned" when entry.EntityType == AuditTimelineEntityType.Client
                    => clientFactory.CreateCardAssignment(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.changed" when entry.EntityType == AuditTimelineEntityType.Client
                    => clientFactory.CreateCardChange(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.cleared" when entry.EntityType == AuditTimelineEntityType.Client
                    => clientFactory.CreateCardClear(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "staff_account.created"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateAccountCreation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.display_name_updated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateDisplayNameUpdate(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.activated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateActivation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.deactivated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateDeactivation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_credentials.configured"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateCredentialConfiguration(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_credentials.reset"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => staffAccountFactory.CreateCredentialReset(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "membership_type.edited"
                    when entry.EntityType == AuditTimelineEntityType.MembershipType
                    => CreateMembershipTypeEdit(before.RootElement, after.RootElement),
                "membership_type.deactivated"
                    when entry.EntityType == AuditTimelineEntityType.MembershipType
                    => CreateMembershipTypeDeactivation(before.RootElement, after.RootElement),
                "membership.issued"
                    when entry.EntityType == AuditTimelineEntityType.Membership
                    => CreateMembershipIssue(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "membership.replaced"
                    when entry.EntityType == AuditTimelineEntityType.Membership
                    => CreateMembershipSaleCorrection(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement,
                        isCancellation: false),
                "membership.sale_canceled"
                    when entry.EntityType == AuditTimelineEntityType.Membership
                    => CreateMembershipSaleCorrection(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement,
                        isCancellation: true),
                "membership_opening_state.created"
                    when entry.EntityType == AuditTimelineEntityType.MembershipOpeningState
                    => CreateMembershipOpeningState(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "membership_negative_closure.created"
                    when entry.EntityType == AuditTimelineEntityType.MembershipNegativeClosure
                    => CreateNegativeClosureCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "membership_negative_closure.canceled"
                    when entry.EntityType == AuditTimelineEntityType.MembershipNegativeClosure
                    => CreateNegativeClosureCorrection(entry, related.RootElement,
                        before.RootElement, after.RootElement, isCancellation: true),
                "membership_negative_closure.replaced"
                    when entry.EntityType == AuditTimelineEntityType.MembershipNegativeClosure
                    => CreateNegativeClosureCorrection(entry, related.RootElement,
                        before.RootElement, after.RootElement, isCancellation: false),
                "non_working_day.added"
                    when entry.EntityType == AuditTimelineEntityType.NonWorkingPeriod
                    => nonWorkingDayFactory.CreateAddition(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "non_working_day.corrected"
                    when entry.EntityType == AuditTimelineEntityType.NonWorkingPeriod
                    => nonWorkingDayFactory.CreateCorrection(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "non_working_day.canceled"
                    when entry.EntityType == AuditTimelineEntityType.NonWorkingPeriod
                    => nonWorkingDayFactory.CreateCancellation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "freeze.added" when entry.EntityType == AuditTimelineEntityType.Freeze
                    => CreateFreezeAddition(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "freeze.canceled" when entry.EntityType == AuditTimelineEntityType.Freeze
                    => CreateFreezeCancellation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "visit.marked" when entry.EntityType == AuditTimelineEntityType.Visit
                    => CreateVisitMarked(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "visit.canceled" when entry.EntityType == AuditTimelineEntityType.Visit
                    => CreateVisitCancellation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "payment.created" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "payment.corrected"
                    when entry.EntityType == AuditTimelineEntityType.Payment
                        && related.RootElement.TryGetProperty(
                            "originalNegativeClosureId",
                            out _)
                    => CreateNegativeClosurePaymentLifecycle(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement,
                        isCancellation: false),
                "payment.canceled"
                    when entry.EntityType == AuditTimelineEntityType.Payment
                        && related.RootElement.TryGetProperty(
                            "originalNegativeClosureId",
                            out _)
                    => CreateNegativeClosurePaymentLifecycle(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement,
                        isCancellation: true),
                "payment.corrected" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCorrection(entry, related.RootElement, before.RootElement, after.RootElement),
                "payment.canceled" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCancellation(entry, related.RootElement, before.RootElement, after.RootElement),
                _ => Unavailable(kind),
            };
        }
        catch (JsonException)
        {
            return Unavailable(kind);
        }
    }

    private AuditEntryExplanationViewModel CreateMembershipTypeCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (entry.EntityId == Guid.Empty
            || related.ValueKind != JsonValueKind.Object
            || related.EnumerateObject().Any()
            || before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any())
        {
            throw new JsonException("Membership type creation summary is inconsistent.");
        }

        var created = ReadMembershipTypeCatalog(after);
        if (!created.HasValidLifecycle()
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                created.CreatedAt,
                entry.RecordedAt)
            || created.UpdatedAt != created.CreatedAt
            || (!created.IsActive && created.DeactivatedAt != created.CreatedAt))
        {
            throw new JsonException("Membership type creation lifecycle is inconsistent.");
        }

        List<AuditEntryExplanationFactViewModel> createdFacts =
        [
            Fact("Membership type", TimelineModel.ShortId(entry.EntityId)),
            .. MembershipTypeFacts(created),
            Fact("Created", TimelineModel.TimestampLabel(created.CreatedAt)),
        ];
        if (created.DeactivatedAt is { } deactivatedAt)
        {
            createdFacts.Add(Fact(
                "Deactivated",
                TimelineModel.TimestampLabel(deactivatedAt)));
        }

        return CreateExplanation("MembershipTypeCreated",
            "membership-type-created",
            [Fact("Membership type", Presentation.Value("NotPresent"))],
            createdFacts,
            ChangedFields: Presentation.Changed("MembershipTypeCatalog"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaperFallbackBatchCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (entry.EntryOrigin != EntryOrigin.PaperFallback
            || entry.EntityId == Guid.Empty
            || before.EnumerateObject().Any()
            || related.EnumerateObject().Any()
            || string.IsNullOrWhiteSpace(entry.Reason)
                && string.IsNullOrWhiteSpace(entry.Comment))
        {
            throw new JsonException("Paper fallback batch audit envelope is inconsistent.");
        }

        var batch = RequireObject(after, "entryBatch");
        var batchId = RequireGuid(batch, "id");
        var batchType = RequireString(batch, "batchType");
        var paperSheetNumber = RequireString(batch, "paperSheetNumber");
        var businessDateStart = RequireDateOnly(batch, "businessDateStart");
        var businessDateEnd = RequireDateOnly(batch, "businessDateEnd");
        var recordedAt = RequireTimestamp(batch, "recordedAt");
        var recordedBy = RequireGuid(batch, "recordedByAccountId");
        var reconciledAt = RequireNullableTimestamp(batch, "reconciledAt");
        var reconciledBy = RequireNullableGuid(batch, "reconciledByAccountId");
        var note = RequireNullableString(batch, "note");
        var occurredBusinessDate = BusinessTimeZone.GetBusinessDate(entry.OccurredAt);

        if (batchId != entry.EntityId
            || batchType != "paper_fallback"
            || paperSheetNumber != paperSheetNumber.Trim().ToUpperInvariant()
            || businessDateStart > businessDateEnd
            || occurredBusinessDate < businessDateStart
            || occurredBusinessDate > businessDateEnd
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                recordedAt,
                entry.RecordedAt)
            || recordedBy != entry.ActorAccountId.Value
            || reconciledAt is not null
            || reconciledBy is not null)
        {
            throw new JsonException("Paper fallback batch audit summary is inconsistent.");
        }

        return CreateExplanation(
            "PaperFallbackBatchCreated",
            "paper-fallback-batch-created",
            [Fact("Paper fallback batch", Presentation.Value("NotPresent"))],
            [
                Fact("Paper fallback batch", TimelineModel.ShortId(batchId)),
                Fact("Paper sheet", paperSheetNumber),
                Fact(
                    "Business dates",
                    Presentation.DateRange(businessDateStart, businessDateEnd)),
                Fact("Recorded", TimelineModel.TimestampLabel(recordedAt)),
                Fact("Recorded by", TimelineModel.ShortId(recordedBy)),
                Fact("Batch note", note ?? Presentation.Value("None")),
            ],
            Presentation.Changed("PaperFallbackBatch"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaperFallbackRowCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (entry.EntryOrigin != EntryOrigin.PaperFallback
            || entry.EntityId == Guid.Empty
            || before.EnumerateObject().Any()
            || string.IsNullOrWhiteSpace(entry.Reason)
                && string.IsNullOrWhiteSpace(entry.Comment))
        {
            throw new JsonException("Paper fallback row audit envelope is inconsistent.");
        }

        var relatedBatchId = RequireGuid(related, "entryBatchId");
        var batch = RequireObject(after, "entryBatch");
        var row = RequireObject(after, "entryBatchRow");
        var batchId = RequireGuid(batch, "id");
        var paperSheetNumber = RequireString(batch, "paperSheetNumber");
        var businessDateStart = RequireDateOnly(batch, "businessDateStart");
        var businessDateEnd = RequireDateOnly(batch, "businessDateEnd");
        var rowId = RequireGuid(row, "id");
        var rowBatchId = RequireGuid(row, "entryBatchId");
        var lineNumber = RequirePositiveInt32(row, "lineNumber");
        var eventType = RequireString(row, "eventType");
        var occurredAt = RequireTimestamp(row, "occurredAt");
        var explanation = RequireString(row, "explanation");
        var recordedAt = RequireTimestamp(row, "recordedAt");
        var recordedBy = RequireGuid(row, "recordedByAccountId");
        var sessionId = RequireGuid(row, "sessionId");
        var supportedEventTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "visit",
            "payment",
            "freeze",
            "membership_sale",
            "negative_coverage",
            "correction_or_cancellation",
        };
        var occurredBusinessDate = BusinessTimeZone.GetBusinessDate(occurredAt);

        if (batchId != relatedBatchId
            || rowId != entry.EntityId
            || rowBatchId != batchId
            || paperSheetNumber != paperSheetNumber.Trim().ToUpperInvariant()
            || businessDateStart > businessDateEnd
            || occurredBusinessDate < businessDateStart
            || occurredBusinessDate > businessDateEnd
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                occurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                recordedAt,
                entry.RecordedAt)
            || recordedBy != entry.ActorAccountId.Value
            || sessionId != entry.SessionId.Value
            || !supportedEventTypes.Contains(eventType)
            || string.IsNullOrWhiteSpace(explanation))
        {
            throw new JsonException("Paper fallback row audit summary is inconsistent.");
        }

        return CreateExplanation(
            "PaperFallbackRowCreated",
            "paper-fallback-row-created",
            [
                Fact("Paper fallback batch", TimelineModel.ShortId(batchId)),
                Fact("Paper row", Presentation.Value("NotPresent")),
            ],
            [
                Fact("Paper fallback batch", TimelineModel.ShortId(batchId)),
                Fact("Paper sheet", paperSheetNumber),
                Fact("Paper row", TimelineModel.ShortId(rowId)),
                Fact("Line number", Presentation.Number(lineNumber)),
                Fact(
                    "Event type",
                    Presentation.Text($"PaperFallback.EventType.{eventType}")),
                Fact("Occurred", TimelineModel.TimestampLabel(occurredAt)),
                Fact("Recorded", TimelineModel.TimestampLabel(recordedAt)),
                Fact("Recorded by", TimelineModel.ShortId(recordedBy)),
                Fact("Session", TimelineModel.ShortId(sessionId)),
                Fact("Explanation", explanation),
            ],
            Presentation.Changed("PaperFallbackRow"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateMembershipTypeEdit(
        JsonElement before,
        JsonElement after)
    {
        var original = ReadMembershipTypeCatalog(before);
        var updated = ReadMembershipTypeCatalog(after);
        var changedFields = MembershipTypeChangedFields(original, updated);

        if (!original.HasValidLifecycle()
            || !updated.HasValidLifecycle()
            || original.IsActive != updated.IsActive
            || original.CreatedAt != updated.CreatedAt
            || original.DeactivatedAt != updated.DeactivatedAt
            || updated.UpdatedAt <= original.UpdatedAt
            || changedFields.Count == 0)
        {
            throw new JsonException("Membership type edit summary is inconsistent.");
        }

        return CreateExplanation("MembershipTypeEdited",
            "membership-type-edited",
            MembershipTypeFacts(original),
            MembershipTypeFacts(updated),
            string.Join(", ", changedFields),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateMembershipOpeningState(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any()
            || entry.EntityId == Guid.Empty
            || entry.EntryOrigin != EntryOrigin.ManualBackfill
            || string.IsNullOrWhiteSpace(entry.Reason))
        {
            throw new JsonException("Membership opening-state envelope is inconsistent.");
        }

        var relatedClientId = RequireGuid(related, "clientId");
        var relatedMembershipId = RequireGuid(related, "membershipId");
        var created = ReadMembershipOpeningStateCreation(after);
        ValidateEntryBatch("manual_backfill", created.EntryBatchId);

        if (created.OpeningStateId != entry.EntityId
            || created.ClientId != relatedClientId
            || created.MembershipId != relatedMembershipId
            || created.Status != "active")
        {
            throw new JsonException("Membership opening-state identity is inconsistent.");
        }

        return CreateExplanation("MembershipOpeningStateCreated",
            "membership-opening-state-created",
            [
                Fact("Opening state", Presentation.Value("NotPresent")),
                Fact("Membership", TimelineModel.ShortId(created.MembershipId)),
            ],
            [
                Fact("Opening state", TimelineModel.ShortId(created.OpeningStateId)),
                Fact("Membership", TimelineModel.ShortId(created.MembershipId)),
                Fact("Client", TimelineModel.ShortId(created.ClientId)),
                Fact("Opening as of", DateLabel(created.OpeningAsOfDate)),
                Fact(
                    "Declared remaining visits",
                    Presentation.Number(created.DeclaredRemainingVisits)),
                Fact(
                    "Declared negative balance",
                    Presentation.Number(created.DeclaredNegativeBalance)),
                Fact(
                    "Known effective end",
                    created.KnownEffectiveEndDate is { } knownEnd
                        ? DateLabel(knownEnd)
                        : Presentation.Value("NotDeclared")),
                Fact(
                    "Known extension",
                    created.KnownExtensionDays is { } knownExtension
                        ? Presentation.Days(knownExtension)
                        : Presentation.Value("NotDeclared")),
                Fact("Source reference", created.SourceReference),
                Fact(
                    "Entry batch",
                    created.EntryBatchId is { } entryBatchId
                        ? TimelineModel.ShortId(entryBatchId)
                        : Presentation.Value("None")),
                Fact("Entry origin", StoredEntryOriginLabel("manual_backfill")),
                Fact("Occurred", TimelineModel.TimestampLabel(entry.OccurredAt)),
                Fact("Source status", StatusLabel(created.Status)),
                Fact(
                    "Recalculated remaining visits",
                    Presentation.Number(
                        created.RecalculatedState.RemainingVisits)),
                Fact(
                    "Recalculated negative balance",
                    Presentation.Number(
                        created.RecalculatedState.NegativeBalance)),
                Fact(
                    "Recalculated effective end",
                    DateLabel(created.RecalculatedState.EffectiveEndDate)),
                Fact(
                    "Recalculated extension",
                    Presentation.Days(
                        created.RecalculatedState.ExtensionDays)),
                Fact(
                    "Recalculation version",
                    Presentation.Number(
                        created.RecalculatedState.RecalculationVersion)),
            ],
            ChangedFields: JoinChanged("OpeningState", "MembershipStateCache"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateNegativeClosureCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (related.TryGetProperty("correctionId", out _))
        {
            return CreateReplacementNegativeClosureCreation(entry, related, before, after);
        }

        if (RequireString(after, "closureType") == "new_membership")
        {
            return CreateNewMembershipNegativeClosureCreation(entry, related, before, after);
        }

        if (entry.EntityId == Guid.Empty)
        {
            throw new JsonException("Negative closure creation summary is inconsistent.");
        }

        var closureId = RequireGuid(after, "negativeClosureId");
        var clientId = RequireGuid(related, "clientId");
        var paymentId = RequireGuid(related, "paymentId");
        _ = RequireGuid(related, "paymentAuditEntryId");
        var sourceMembershipIds = RequireGuidArray(
            related,
            "sourceMembershipIds");
        var relatedVisitIds = RequireGuidArray(related, "visitIds");
        var totalNegativeBalance = RequirePositiveInt32(
            before,
            "totalNegativeBalance");
        var openConcreteVisitCount = RequirePositiveInt32(
            before,
            "openConcreteVisitCount");
        var unknownNegativeBalance = RequireNonNegativeInt32(
            before,
            "unknownNegativeBalance");
        var oldestOpenNegativeVisitId = RequireGuid(
            before,
            "oldestOpenNegativeVisitId");
        var closureType = RequireString(after, "closureType");
        var visitsCount = RequirePositiveInt32(after, "visitsCount");
        var lines = ReadNegativeClosureLines(after);
        var coveredVisitIds = RequireGuidArray(after, "coveredVisitIds");
        var remainingNegativeBalance = RequireNonNegativeInt32(
            after,
            "remainingNegativeBalance");
        var occurredAt = RequireTimestamp(after, "occurredAt");
        var recordedAt = RequireTimestamp(after, "recordedAt");
        var entryOrigin = RequireString(after, "entryOrigin");
        var entryBatchId = RequireNullableGuid(after, "entryBatchId");
        var status = RequireString(after, "status");
        var payment = RequireObject(after, "payment");
        var paymentAmount = RequireDecimal(payment, "amount");
        var paymentCurrency = RequireString(payment, "currency");
        ValidateEntryBatch(entryOrigin, entryBatchId);
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            entryBatchId,
            "Negative closure");

        if (closureId != entry.EntityId
            || closureType != "one_off"
            || status != "active"
            || clientId == Guid.Empty
            || sourceMembershipIds.Count == 0
            || sourceMembershipIds.Distinct().Count() != sourceMembershipIds.Count
            || coveredVisitIds.Count != visitsCount
            || coveredVisitIds.Distinct().Count() != coveredVisitIds.Count
            || !relatedVisitIds.SequenceEqual(coveredVisitIds)
            || coveredVisitIds[0] != oldestOpenNegativeVisitId
            || totalNegativeBalance
                != openConcreteVisitCount + unknownNegativeBalance
            || visitsCount > openConcreteVisitCount
            || remainingNegativeBalance != totalNegativeBalance - visitsCount
            || lines.Sum(line => line.Quantity) != visitsCount
            || lines.Any(line => line.Currency != paymentCurrency)
            || lines.Sum(line => line.LineTotal) != paymentAmount
            || RequireGuid(payment, "paymentId") != paymentId
            || RequireString(payment, "context") != "negative_closure"
            || RequireString(payment, "method") != "cash"
            || entryOrigin != EntryOriginValue(entry.EntryOrigin)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(occurredAt, entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(recordedAt, entry.RecordedAt))
        {
            throw new JsonException("Negative closure creation summary identity is inconsistent.");
        }

        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Negative closure", TimelineModel.ShortId(closureId)),
            Fact("Payment record", TimelineModel.ShortId(paymentId)),
            Fact("Closure type", Presentation.Text("ClosureType.OneOff")),
            Fact("Covered visits", Presentation.Number(visitsCount)),
            Fact("Amount", MoneyLabel(
                paymentAmount,
                paymentCurrency)),
            Fact(
                "Negative balance",
                Presentation.Number(remainingNegativeBalance)),
            Fact("Occurred", TimelineModel.TimestampLabel(occurredAt)),
            Fact("Entry origin", StoredEntryOriginLabel(entryOrigin)),
        };
        if (paperReference is not null)
        {
            afterFacts.Add(Fact("Paper fallback batch", TimelineModel.ShortId(paperReference.EntryBatchId)));
            afterFacts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
            afterFacts.Add(Fact("Paper row", TimelineModel.ShortId(paperReference.EntryBatchRowId)));
            afterFacts.Add(Fact("Line number", Presentation.Number(paperReference.LineNumber)));
            afterFacts.Add(Fact("Event type", Presentation.Text("PaperFallback.EventType.negative_coverage")));
            afterFacts.Add(Fact("Explanation", paperReference.Explanation));
        }

        return CreateExplanation(
            "MembershipNegativeClosureCreated",
            "membership-negative-closure-created",
            [Fact("Negative closure", Presentation.Value("NotPresent"))],
            afterFacts,
            Presentation.Changed("NegativeVisitCoverage"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateNewMembershipNegativeClosureCreation(
        AuditTimelineEntry entry, JsonElement related, JsonElement before, JsonElement after)
    {
        var closureId = RequireGuid(after, "negativeClosureId");
        var clientId = RequireGuid(related, "clientId");
        var coveringMembershipId = RequireGuid(related, "coveringMembershipId");
        var salePaymentId = RequireGuid(related, "salePaymentId");
        _ = RequireGuid(related, "salePaymentAuditEntryId");
        var relatedVisits = RequireGuidArray(related, "visitIds");
        var sourceMembershipIds = RequireGuidArray(related, "sourceMembershipIds");
        var visits = RequireGuidArray(after, "coveredVisitIds");
        var count = RequirePositiveInt32(after, "coveredVisitCount");
        var total = RequirePositiveInt32(before, "totalNegativeBalance");
        var open = RequirePositiveInt32(before, "openConcreteVisitCount");
        var unknown = RequireNonNegativeInt32(before, "unknownNegativeBalance");
        var oldest = RequireGuid(before, "oldestOpenNegativeVisitId");
        var origin = RequireString(after, "entryOrigin");
        var batch = ReadOptionalGuid(after, "entryBatchId");
        var occurred = RequireTimestamp(after, "occurredAt");
        var recorded = RequireTimestamp(after, "recordedAt");
        var forcedStartDate = RequireDateOnly(after, "forcedStartDate");
        var coveringState = RequireObject(after, "coveringMembershipState");
        var countedVisits = RequireNonNegativeInt32(coveringState, "countedVisits");
        _ = RequireNonNegativeInt32(coveringState, "remainingVisits");
        var effectiveEndDate = RequireDateOnly(coveringState, "effectiveEndDate");
        _ = RequireTimestamp(coveringState, "lastCountedVisitAt");
        var paper = ReadPaperReference(related, entry.EntryOrigin, batch, "Negative closure");
        ValidateEntryBatch(origin, batch);
        if (closureId != entry.EntityId
            || clientId == Guid.Empty
            || coveringMembershipId == Guid.Empty
            || salePaymentId == Guid.Empty
            || RequireGuid(after, "coveringMembershipId") != coveringMembershipId
            || RequireString(after, "closureType") != "new_membership"
            || RequireString(after, "status") != "active"
            || visits.Count != count
            || visits.Distinct().Count() != count
            || !relatedVisits.SequenceEqual(visits)
            || sourceMembershipIds.Count == 0
            || sourceMembershipIds.Distinct().Count() != sourceMembershipIds.Count
            || visits[0] != oldest
            || total != open + unknown
            || count > open
            || RequireNonNegativeInt32(after, "remainingNegativeBalance") != total - count
            || countedVisits != count
            || effectiveEndDate < forcedStartDate
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(occurred, entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(recorded, entry.RecordedAt)
            || origin != EntryOriginValue(entry.EntryOrigin))
        {
            throw new JsonException("New-Membership negative closure creation is inconsistent.");
        }

        var facts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Negative closure", TimelineModel.ShortId(closureId)),
            Fact("Membership", TimelineModel.ShortId(coveringMembershipId)),
            Fact("Payment record", TimelineModel.ShortId(salePaymentId)),
            Fact("Closure type", Presentation.Text("ClosureType.NewMembership")),
            Fact("Covered visits", Presentation.Number(count)),
            Fact("Negative balance", Presentation.Number(RequireNonNegativeInt32(after, "remainingNegativeBalance"))),
            Fact("Occurred", TimelineModel.TimestampLabel(occurred)),
            Fact("Entry origin", StoredEntryOriginLabel(origin)),
        };
        AddPaperReferenceFacts(facts, paper, PaperFallbackEventType.MembershipSale);
        return CreateExplanation("MembershipNegativeClosureCreatedNewMembership", "membership-negative-closure-created",
            [Fact("Negative closure", Presentation.Value("NotPresent"))], facts,
            Presentation.Changed("NegativeVisitCoverage"), IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateReplacementNegativeClosureCreation(
        AuditTimelineEntry entry, JsonElement related, JsonElement before, JsonElement after)
    {
        var closureId = RequireGuid(after, "negativeClosureId");
        var correctionId = RequireGuid(related, "correctionId");
        var originalId = RequireGuid(related, "originalNegativeClosureId");
        var clientId = RequireGuid(related, "clientId");
        var relatedVisits = RequireGuidArray(related, "visitIds");
        var sources = RequireGuidArray(related, "sourceMembershipIds");
        var visits = RequireGuidArray(after, "coveredVisitIds");
        var count = RequirePositiveInt32(after, "visitsCount");
        var restored = RequirePositiveInt32(before, "restoredNegativeBalance");
        var oldest = RequireGuid(before, "oldestOpenConcreteVisitId");
        var type = RequireString(after, "closureType");
        var origin = RequireString(after, "entryOrigin");
        var batch = RequireNullableGuid(after, "entryBatchId");
        var occurred = RequireTimestamp(after, "occurredAt");
        var recorded = RequireTimestamp(after, "recordedAt");
        var paymentId = RequireNullableGuid(after, "replacementPaymentId");
        var paymentAuditId = RequireNullableGuid(
            related,
            "replacementPaymentAuditEntryId");
        var summarizedPaymentAuditId = RequireNullableGuid(
            after,
            "replacementPaymentAuditEntryId");
        var replacementPaymentElement = RequireNullableObject(
            after,
            "replacementPayment");
        var replacementPayment = replacementPaymentElement is null
            ? null
            : ReadNegativeClosureLifecyclePayment(
                replacementPaymentElement.Value);
        var coveringMembershipId = RequireNullableGuid(after, "coveringMembershipId");
        var paper = ReadPaperReference(related, entry.EntryOrigin, batch, "Negative closure");
        ValidateEntryBatch(origin, batch);
        IReadOnlyList<NegativeClosureAuditLineSnapshot> lines;
        if (type == "one_off")
        {
            lines = ReadNegativeClosureLines(after);
        }
        else
        {
            if (!after.TryGetProperty("lines", out var newMembershipLines)
                || newMembershipLines.ValueKind != JsonValueKind.Array
                || newMembershipLines.GetArrayLength() != 0)
            {
                throw new JsonException(
                    "New-Membership replacement cannot include one-off lines.");
            }

            lines = [];
        }
        if (closureId != entry.EntityId
            || closureId == originalId
            || correctionId == Guid.Empty
            || originalId == Guid.Empty
            || clientId == Guid.Empty
            || visits.Count != count
            || visits.Distinct().Count() != count
            || !relatedVisits.SequenceEqual(visits)
            || sources.Count == 0
            || sources.Distinct().Count() != sources.Count
            || visits[0] != oldest
            || RequireNonNegativeInt32(after, "remainingNegativeBalance") != restored - count
            || RequireString(after, "status") != "active"
            || RequireBoolean(after, "changedAfterClose")
                != entry.ChangedAfterClose
            || origin != EntryOriginValue(entry.EntryOrigin)
            || RequireGuid(related, "correctionId") != correctionId
            || RequireGuid(related, "originalNegativeClosureId") != originalId
            || RequireNullableGuid(related, "replacementPaymentId") != paymentId
            || summarizedPaymentAuditId != paymentAuditId
            || RequireNullableGuid(related, "coveringMembershipId") != coveringMembershipId
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(occurred, entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(recorded, entry.RecordedAt)
            || (type == "one_off"
                && (paymentId is null
                    || paymentAuditId is null
                    || replacementPayment is null
                    || coveringMembershipId is not null
                    || lines.Count == 0
                    || lines.Sum(line => line.Quantity) != count
                    || replacementPayment.PaymentId != paymentId
                    || replacementPayment.ClientId != clientId
                    || replacementPayment.NegativeClosureId != closureId
                    || replacementPayment.Amount
                        != lines.Sum(line => line.LineTotal)
                    || lines.Any(
                        line => line.Currency != replacementPayment.Currency)
                    || replacementPayment.Method != "cash"
                    || replacementPayment.PaymentContext
                        != "negative_closure"
                    || replacementPayment.Status != "active"
                    || replacementPayment.EntryOrigin != origin
                    || replacementPayment.EntryBatchId != batch
                    || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                        replacementPayment.OccurredAt,
                        occurred)
                    || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                        replacementPayment.RecordedAt,
                        recorded)))
            || (type == "new_membership"
                && (paymentId is not null
                    || paymentAuditId is not null
                    || replacementPayment is not null
                    || coveringMembershipId is null
                    || lines.Count != 0))
            || (type is not ("one_off" or "new_membership")))
        {
            throw new JsonException("Replacement negative closure creation is inconsistent.");
        }

        var facts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Negative closure", TimelineModel.ShortId(closureId)),
            Fact("Correction", TimelineModel.ShortId(correctionId)),
            Fact("Closure type", type == "one_off" ? Presentation.Text("ClosureType.OneOff") : Presentation.Text("ClosureType.NewMembership")),
            Fact("Covered visits", Presentation.Number(count)),
            Fact("Negative balance", Presentation.Number(RequireNonNegativeInt32(after, "remainingNegativeBalance"))),
        };
        if (paymentId is { } payment) facts.Add(Fact("Payment record", TimelineModel.ShortId(payment)));
        if (coveringMembershipId is { } membership) facts.Add(Fact("Membership", TimelineModel.ShortId(membership)));
        AddPaperReferenceFacts(facts, paper, PaperFallbackEventType.CorrectionOrCancellation);
        var resourceKey = type == "new_membership"
            ? "MembershipNegativeClosureCreatedNewMembershipCorrection"
            : "MembershipNegativeClosureCreated";
        return CreateExplanation(resourceKey, "membership-negative-closure-created",
            [Fact("Negative closure", TimelineModel.ShortId(originalId))], facts,
            Presentation.Changed("NegativeVisitCoverage"), IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateNegativeClosureCorrection(
        AuditTimelineEntry entry, JsonElement related, JsonElement before, JsonElement after, bool isCancellation)
    {
        var correction = RequireObject(after, "correction");
        var original = RequireObject(before, "closure");
        var correctionId = RequireGuid(correction, "correctionId");
        var originalId = RequireGuid(original, "id");
        var oldestVisitId = RequireGuid(
            original,
            "oldestOpenNegativeVisitId");
        var type = RequireString(original, "closureType");
        var originalVisits = RequirePositiveInt32(original, "visitsCount");
        var originalItems = RequireGuidArray(before, "itemIds");
        var originalVisitIds = RequireGuidArray(before, "visitIds");
        var beforeVisible = RequireNonNegativeInt32(before, "visibleNegativeBalance");
        var mode = RequireString(correction, "mode");
        var origin = RequireString(correction, "entryOrigin");
        var batch = RequireNullableGuid(correction, "entryBatchId");
        var replacement = RequireNullableObject(after, "replacement");
        var relatedReplacementId = RequireNullableGuid(related, "replacementNegativeClosureId");
        var relatedReplacementAuditId = RequireNullableGuid(related, "replacementClosureAuditId");
        var relatedOriginalPaymentId = RequireNullableGuid(related, "originalPaymentId");
        var relatedReplacementPaymentId = RequireNullableGuid(related, "replacementPaymentId");
        var paymentLifecycleAuditId = RequireNullableGuid(related, "paymentLifecycleAuditId");
        var relatedMembershipIds = RequireGuidArray(related, "membershipIds");
        var paper = ReadPaperReference(related, entry.EntryOrigin, batch, "Negative closure correction");
        ValidateEntryBatch(origin, batch);
        var expectedStatus = isCancellation ? "canceled" : "replaced";
        var clientId = RequireGuid(original, "clientId");
        var coveringMembershipId = RequireNullableGuid(original, "coveringMembershipId");
        var originalLines = ReadNegativeClosureCorrectionLines(before);
        var originalPaymentElement = RequireNullableObject(before, "payment");
        var originalPayment = originalPaymentElement is null
            ? null
            : ReadNegativeClosureLifecyclePayment(originalPaymentElement.Value);
        if (entry.EntityId != originalId
            || RequireGuid(related, "clientId") != clientId
            || RequireGuid(related, "correctionId") != correctionId
            || mode != (isCancellation ? "cancel" : "replace")
            || RequireString(correction, "reason") != entry.Reason
            || origin != EntryOriginValue(entry.EntryOrigin)
            || RequireBoolean(correction, "changedAfterClose") != entry.ChangedAfterClose
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(RequireTimestamp(correction, "occurredAt"), entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(RequireTimestamp(correction, "recordedAt"), entry.RecordedAt)
            || RequireString(original, "status") != "active"
            || RequireString(RequireObject(after, "originalClosure"), "status") != expectedStatus
            || RequireGuid(RequireObject(after, "originalClosure"), "id") != originalId
            || RequireString(RequireObject(after, "originalClosure"), "closureType") != type
            || originalItems.Count != originalVisits
            || originalItems.Distinct().Count() != originalVisits
            || originalVisitIds.Count != originalVisits
            || originalVisitIds.Distinct().Count() != originalVisits
            || originalVisitIds[0] != oldestVisitId
            || relatedMembershipIds.Count == 0
            || relatedMembershipIds.Distinct().Count() != relatedMembershipIds.Count
            || (type == "one_off"
                && (coveringMembershipId is not null
                    || originalPayment is null
                    || relatedOriginalPaymentId != originalPayment.PaymentId
                    || paymentLifecycleAuditId is null
                    || originalPayment.ClientId != clientId
                    || originalPayment.NegativeClosureId != originalId
                    || originalPayment.Amount != originalLines.Sum(line => line.LineTotal)
                    || originalLines.Count == 0
                    || originalLines.Sum(line => line.Quantity) != originalVisits
                    || originalLines.Any(line => line.Currency != originalPayment.Currency)
                    || originalPayment.Method != "cash"
                    || originalPayment.PaymentContext != "negative_closure"
                    || originalPayment.Status != "active"))
            || (type == "new_membership"
                && (coveringMembershipId is null
                    || originalPayment is not null
                    || relatedOriginalPaymentId is not null
                    || paymentLifecycleAuditId is not null
                    || originalLines.Count != 0
                    || entry.ChangedAfterClose))
            || (type is not ("one_off" or "new_membership")))
        {
            throw new JsonException("Negative closure correction identity is inconsistent.");
        }
        var replacementVisits = 0;
        if (isCancellation
                ? replacement is not null
                    || relatedReplacementId is not null
                    || relatedReplacementAuditId is not null
                    || relatedReplacementPaymentId is not null
                : replacement is null
                    || relatedReplacementId is null
                    || relatedReplacementAuditId is null)
        {
            throw new JsonException("Negative closure correction replacement shape is inconsistent.");
        }
        if (replacement is not null)
        {
            replacementVisits = RequirePositiveInt32(replacement.Value, "visitsCount");
            var replacementId = RequireGuid(replacement.Value, "negativeClosureId");
            var paymentId = RequireNullableGuid(replacement.Value, "paymentId");
            var visitIds = RequireGuidArray(replacement.Value, "visitIds");
            if (replacementId != relatedReplacementId
                || visitIds.Count != replacementVisits
                || visitIds.Distinct().Count() != replacementVisits
                || (type == "one_off" && paymentId is null)
                || (type == "new_membership" && paymentId is not null)
                || relatedReplacementPaymentId != paymentId)
            {
                throw new JsonException("Negative closure replacement facts are inconsistent.");
            }
        }
        if (RequireNonNegativeInt32(after, "remainingNegativeBalance") != beforeVisible + originalVisits - replacementVisits)
        {
            throw new JsonException("Negative closure correction balance is inconsistent.");
        }
        var facts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Negative closure", TimelineModel.ShortId(originalId)),
            Fact("Correction", TimelineModel.ShortId(correctionId)),
            Fact("Original status", Presentation.Status(isCancellation ? "Canceled" : "Replaced")),
            Fact("Negative balance", Presentation.Number(RequireNonNegativeInt32(after, "remainingNegativeBalance"))),
        };
        if (replacement is { } item) facts.Add(Fact("Replacement", TimelineModel.ShortId(RequireGuid(item, "negativeClosureId"))));
        AddPaperReferenceFacts(facts, paper, PaperFallbackEventType.CorrectionOrCancellation);
        return CreateExplanation(isCancellation ? "MembershipNegativeClosureCanceled" : "MembershipNegativeClosureReplaced",
            isCancellation ? "membership-negative-closure-canceled" : "membership-negative-closure-replaced",
            [Fact("Negative closure", TimelineModel.ShortId(originalId))], facts,
            Presentation.Changed("NegativeVisitCoverage"), IsAvailable: true);
    }

    private static IReadOnlyList<NegativeClosureCorrectionLineSnapshot>
        ReadNegativeClosureCorrectionLines(JsonElement before)
    {
        if (!before.TryGetProperty("lines", out var lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Negative closure correction requires original line summaries.");
        }

        var result = new List<NegativeClosureCorrectionLineSnapshot>();
        foreach (var line in lines.EnumerateArray())
        {
            var quantity = RequirePositiveInt32(line, "quantity");
            var unitPrice = RequireDecimal(line, "unitPriceAmountSnapshot");
            var lineTotal = RequireDecimal(line, "lineTotal");
            if (unitPrice <= 0 || lineTotal != unitPrice * quantity)
            {
                throw new JsonException(
                    "Negative closure correction line arithmetic is inconsistent.");
            }

            result.Add(new NegativeClosureCorrectionLineSnapshot(
                RequireGuid(line, "id"),
                RequireGuid(line, "membershipTypeId"),
                RequireString(line, "typeNameSnapshot"),
                quantity,
                unitPrice,
                RequireString(line, "currencySnapshot"),
                lineTotal));
        }

        if (result.Select(line => line.LineId).Distinct().Count() != result.Count)
        {
            throw new JsonException(
                "Negative closure correction line identities are inconsistent.");
        }

        return result.AsReadOnly();
    }

    private static NegativeClosureLifecyclePaymentSnapshot
        ReadNegativeClosureLifecyclePayment(JsonElement payment)
    {
        var amount = RequireDecimal(payment, "amount");
        if (amount <= 0)
        {
            throw new JsonException(
                "Negative closure lifecycle Payment amount must be positive.");
        }

        return new NegativeClosureLifecyclePaymentSnapshot(
            RequireGuid(payment, "paymentId"),
            RequireGuid(payment, "clientId"),
            RequireGuid(payment, "negativeClosureId"),
            amount,
            RequireString(payment, "currency"),
            RequireString(payment, "method"),
            RequireString(payment, "paymentContext"),
            RequireTimestamp(payment, "occurredAt"),
            RequireTimestamp(payment, "recordedAt"),
            RequireString(payment, "entryOrigin"),
            RequireNullableGuid(payment, "entryBatchId"),
            RequireNullableString(payment, "comment"),
            RequireString(payment, "status"));
    }

    private static IReadOnlyList<NegativeClosureAuditLineSnapshot>
        ReadNegativeClosureLines(JsonElement summary)
    {
        if (!summary.TryGetProperty("lines", out var lines)
            || lines.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                "Negative closure creation requires line summaries.");
        }

        var result = new List<NegativeClosureAuditLineSnapshot>();
        foreach (var line in lines.EnumerateArray())
        {
            if (line.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    "Negative closure line summary is inconsistent.");
            }

            var quantity = RequirePositiveInt32(line, "quantity");
            var unitPrice = RequireDecimal(line, "unitPriceAmount");
            var lineTotal = RequireDecimal(line, "lineTotal");
            result.Add(new NegativeClosureAuditLineSnapshot(
                RequireGuid(line, "lineId"),
                RequirePositiveInt32(line, "sequence"),
                RequireGuid(line, "membershipTypeId"),
                RequireString(line, "typeName"),
                quantity,
                unitPrice,
                RequireString(line, "currency"),
                lineTotal));
        }

        if (result.Count == 0
            || result.Select(line => line.LineId).Distinct().Count() != result.Count
            || result.Select(line => line.MembershipTypeId).Distinct().Count()
                != result.Count
            || !result.Select(line => line.Sequence)
                .SequenceEqual(Enumerable.Range(1, result.Count))
            || result.Any(line => line.LineTotal != line.UnitPrice * line.Quantity))
        {
            throw new JsonException(
                "Negative closure line summaries are inconsistent.");
        }

        return result;
    }

    private AuditEntryExplanationViewModel CreateMembershipTypeDeactivation(
        JsonElement before,
        JsonElement after)
    {
        var original = ReadMembershipTypeCatalog(before);
        var deactivated = ReadMembershipTypeCatalog(after);

        if (!original.HasValidLifecycle()
            || !deactivated.HasValidLifecycle()
            || !original.IsActive
            || deactivated.IsActive
            || original.CatalogValues() != deactivated.CatalogValues()
            || original.CreatedAt != deactivated.CreatedAt
            || original.DeactivatedAt is not null
            || deactivated.DeactivatedAt != deactivated.UpdatedAt
            || deactivated.UpdatedAt <= original.UpdatedAt)
        {
            throw new JsonException("Membership type deactivation summary is inconsistent.");
        }

        return CreateExplanation("MembershipTypeDeactivated",
            "membership-type-deactivated",
            MembershipTypeFacts(original),
            [
                .. MembershipTypeFacts(deactivated),
                Fact(
                    "Deactivated",
                    TimelineModel.TimestampLabel(deactivated.DeactivatedAt.Value)),
            ],
            ChangedFields: Presentation.Changed("CatalogStatus"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateVisitMarked(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var relatedClientId = RequireGuid(related, "clientId");
        var relatedMembershipId = RequireNullableGuid(related, "membershipId");
        var relatedConsumptionId = RequireNullableGuid(related, "consumptionId");
        var visit = ReadMarkedVisit(RequireObject(after, "visit"));
        var afterStateElement = RequireNullableObject(after, "membershipState");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            visit.EntryBatchId,
            "Visit");

        ValidateEntryBatch(visit.EntryOrigin, visit.EntryBatchId);
        if (visit.VisitId != entry.EntityId
            || visit.ClientId != relatedClientId
            || visit.MembershipId != relatedMembershipId
            || visit.ConsumptionId != relatedConsumptionId
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                visit.OccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                visit.RecordedAt,
                entry.RecordedAt)
            || visit.EntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || visit.Comment != entry.Comment
            || visit.Status != "active")
        {
            throw new JsonException("Marked Visit summary identity is inconsistent.");
        }

        VisitMarkedMembershipStateSnapshot? beforeState = null;
        VisitMarkedMembershipStateSnapshot? afterState = null;
        if (visit.VisitKind == "membership")
        {
            beforeState = ReadVisitMarkedMembershipState(before);
            afterState = afterStateElement is null
                ? throw new JsonException(
                    "A Membership Visit requires stored Membership state.")
                : ReadVisitMarkedMembershipState(afterStateElement.Value);

            if (visit.MembershipId is null
                || visit.ConsumptionId is null
                || visit.Selection != "explicit_membership"
                || beforeState.MembershipId != visit.MembershipId
                || afterState.MembershipId != visit.MembershipId
                || beforeState.ExtensionDays != afterState.ExtensionDays
                || beforeState.EffectiveEndDate != afterState.EffectiveEndDate)
            {
                throw new JsonException(
                    "Membership Visit state or consumption is inconsistent.");
            }
        }
        else if (visit.VisitKind is "one_off" or "trial")
        {
            if (before.ValueKind != JsonValueKind.Object
                || before.EnumerateObject().Any()
                || afterStateElement is not null
                || visit.MembershipId is not null
                || visit.ConsumptionId is not null
                || visit.Selection != "explicit_non_membership_context"
                || visit.Acknowledgements.Count != 0)
            {
                throw new JsonException(
                    "A non-membership Visit cannot include Membership state or consumption.");
            }
        }
        else
        {
            throw new JsonException("Visit kind is not supported.");
        }

        var visitKindLabel = VisitKindLabel(visit.VisitKind);
        var acknowledgementLabel = VisitAcknowledgementsLabel(
            visit.Acknowledgements);
        List<AuditEntryExplanationFactViewModel> beforeFacts =
        [
            Fact("Visit", Presentation.Value("NotPresent")),
            Fact("Membership", OptionalIdLabel(visit.MembershipId)),
            Fact(
                "Consumption",
                visit.ConsumptionId is null
                    ? Presentation.Value("NotApplicable")
                    : Presentation.Value("NotPresent")),
        ];
        AddVisitMarkedMembershipFacts(beforeFacts, beforeState);

        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Visit type", visitKindLabel),
            Fact("Visit", TimelineModel.ShortId(visit.VisitId)),
            Fact("Client", TimelineModel.ShortId(visit.ClientId)),
            Fact("Occurred", TimelineModel.TimestampLabel(visit.OccurredAt)),
            Fact("Status", Presentation.Status("Active")),
            Fact("Membership", OptionalIdLabel(visit.MembershipId)),
            Fact(
                "Consumption",
                visit.ConsumptionId is { } consumptionId
                    ? Presentation.Text(
                        "Template.CountedId",
                        TimelineModel.ShortId(consumptionId))
                    : Presentation.Value("NotApplicable")),
            Fact("Selection", VisitSelectionLabel(visit.VisitKind)),
            Fact("Warning acknowledgements", acknowledgementLabel),
        ];
        if (paperReference is not null)
        {
            afterFacts.Add(Fact(
                "Paper fallback batch",
                TimelineModel.ShortId(paperReference.EntryBatchId)));
            afterFacts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
            afterFacts.Add(Fact(
                "Paper row",
                TimelineModel.ShortId(paperReference.EntryBatchRowId)));
            afterFacts.Add(Fact(
                "Line number",
                Presentation.Number(paperReference.LineNumber)));
            afterFacts.Add(Fact("Explanation", paperReference.Explanation));
        }
        AddVisitMarkedMembershipFacts(afterFacts, afterState);

        var isMembershipVisit = visit.MembershipId is not null;
        return CreateExplanation(isMembershipVisit ? "VisitMarked.Membership" : visit.VisitKind == "one_off" ? "VisitMarked.OneOff" : "VisitMarked.Trial",
            "visit-marked",
            beforeFacts,
            afterFacts,
            ChangedFields: isMembershipVisit
                ? JoinChanged("Visit", "CountedConsumption", "MembershipState")
                : Presentation.Changed("VisitOnly"),
            IsAvailable: true);
    }

    private static PaperReferenceSnapshot? ReadPaperReference(
        JsonElement related,
        EntryOrigin entryOrigin,
        Guid? sourceEntryBatchId,
        string sourceName)
    {
        var entryBatchId = ReadOptionalGuid(related, "entryBatchId");
        var entryBatchRowId = ReadOptionalGuid(related, "entryBatchRowId");
        var paperSheetNumber = ReadOptionalString(related, "paperSheetNumber");
        var lineNumber = ReadOptionalInt32(related, "lineNumber");
        var explanation = ReadOptionalString(related, "paperExplanation");

        if (entryOrigin != EntryOrigin.PaperFallback)
        {
            if (entryBatchId is not null
                || entryBatchRowId is not null
                || paperSheetNumber is not null
                || lineNumber is not null
                || explanation is not null)
            {
                throw new JsonException(
                    $"A non-paper {sourceName} cannot include paper row provenance.");
            }

            return null;
        }

        if (entryBatchId is null
            || entryBatchRowId is null
            || entryBatchId == Guid.Empty
            || entryBatchRowId == Guid.Empty
            || sourceEntryBatchId != entryBatchId
            || string.IsNullOrWhiteSpace(paperSheetNumber)
            || paperSheetNumber != paperSheetNumber.Trim().ToUpperInvariant()
            || lineNumber is null or <= 0
            || string.IsNullOrWhiteSpace(explanation)
            || explanation != explanation.Trim())
        {
            throw new JsonException(
                $"Paper {sourceName} row provenance is inconsistent.");
        }

        return new PaperReferenceSnapshot(
            entryBatchId.Value,
            entryBatchRowId.Value,
            paperSheetNumber,
            lineNumber.Value,
            explanation);
    }

    private void AddPaperReferenceFacts(
        ICollection<AuditEntryExplanationFactViewModel> facts,
        PaperReferenceSnapshot? paperReference,
        PaperFallbackEventType eventType)
    {
        if (paperReference is null)
        {
            return;
        }

        facts.Add(Fact("Paper fallback batch", TimelineModel.ShortId(paperReference.EntryBatchId)));
        facts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
        facts.Add(Fact("Paper row", TimelineModel.ShortId(paperReference.EntryBatchRowId)));
        facts.Add(Fact("Line number", Presentation.Number(paperReference.LineNumber)));
        facts.Add(Fact("Event type", PaperEventTypeLabel(eventType)));
        facts.Add(Fact("Explanation", paperReference.Explanation));
    }

    private string PaperEventTypeLabel(PaperFallbackEventType eventType) =>
        Presentation.Text(eventType switch
        {
            PaperFallbackEventType.Visit => "PaperFallback.EventType.visit",
            PaperFallbackEventType.Payment => "PaperFallback.EventType.payment",
            PaperFallbackEventType.Freeze => "PaperFallback.EventType.freeze",
            PaperFallbackEventType.MembershipSale =>
                "PaperFallback.EventType.membership_sale",
            PaperFallbackEventType.NegativeCoverage =>
                "PaperFallback.EventType.negative_coverage",
            PaperFallbackEventType.CorrectionOrCancellation =>
                "PaperFallback.EventType.correction_or_cancellation",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                "Unsupported paper fallback event type."),
        });

    private static Guid? ReadOptionalGuid(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var parsed)
            ? parsed
            : throw new JsonException($"'{propertyName}' must be a UUID.");
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new JsonException($"'{propertyName}' must be text.");
    }

    private static int? ReadOptionalInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new JsonException($"'{propertyName}' must be an integer.");
    }

    private AuditEntryExplanationViewModel CreateVisitCancellation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var originalVisit = RequireObject(before, "visit");
        var canceledVisit = RequireObject(after, "visit");
        var cancellation = RequireObject(after, "cancellation");

        var visitId = RequireGuid(originalVisit, "visitId");
        var clientId = RequireGuid(originalVisit, "clientId");
        var cancellationId = RequireGuid(cancellation, "cancellationId");
        var cancellationReason = RequireString(cancellation, "reason");
        var cancellationOccurredAt = RequireTimestamp(cancellation, "occurredAt");
        var cancellationRecordedAt = RequireTimestamp(cancellation, "recordedAt");
        var cancellationEntryOrigin = RequireString(cancellation, "entryOrigin");
        var cancellationEntryBatchId = RequireNullableGuid(
            cancellation,
            "entryBatchId");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            cancellationEntryBatchId,
            "Visit cancellation");

        ValidateEntryBatch(cancellationEntryOrigin, cancellationEntryBatchId);
        if (visitId != entry.EntityId
            || RequireGuid(canceledVisit, "visitId") != visitId
            || RequireGuid(cancellation, "visitId") != visitId
            || cancellationId == Guid.Empty
            || RequireGuid(related, "clientId") != clientId
            || RequireGuid(related, "cancellationId") != cancellationId
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                cancellationOccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                cancellationRecordedAt,
                entry.RecordedAt)
            || cancellationEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || cancellationReason != entry.Reason
            || RequireString(originalVisit, "status") != "active"
            || RequireString(canceledVisit, "status") != "canceled")
        {
            throw new JsonException("Visit cancellation summary identity is inconsistent.");
        }

        var membershipId = RequireNullableGuid(originalVisit, "membershipId");
        if (RequireNullableGuid(related, "membershipId") != membershipId)
        {
            throw new JsonException(
                "Visit cancellation Membership reference is inconsistent.");
        }

        var originalConsumptionStatus = RequireNullableString(
            originalVisit,
            "consumptionStatus");
        var canceledConsumptionStatus = RequireNullableString(
            canceledVisit,
            "consumptionStatus");
        var consumptionId = RequireNullableGuid(originalVisit, "consumptionId");
        if (RequireNullableGuid(canceledVisit, "consumptionId") != consumptionId
            || RequireNullableGuid(related, "activeConsumptionId") != consumptionId)
        {
            throw new JsonException(
                "Visit cancellation consumption reference is inconsistent.");
        }

        var beforeMembership = ReadMembershipState(before, membershipId);
        var afterMembership = ReadMembershipState(after, membershipId);

        var beforeFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Visit type", VisitKindLabel(RequireString(originalVisit, "visitKind"))),
            Fact("Status", Presentation.Status("Active")),
            Fact("Occurred", TimelineModel.TimestampLabel(
                RequireTimestamp(originalVisit, "occurredAt"))),
            Fact("Membership", OptionalIdLabel(membershipId)),
            Fact("Consumption", ConsumptionStatusLabel(originalConsumptionStatus)),
        };
        AddMembershipFacts(beforeFacts, beforeMembership);

        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Original fact", Presentation.Value("Preserved")),
            Fact("Status", Presentation.Status("Canceled")),
            Fact("Membership", OptionalIdLabel(membershipId)),
            Fact("Consumption", ConsumptionStatusLabel(canceledConsumptionStatus)),
        };
        if (paperReference is not null)
        {
            afterFacts.Add(Fact(
                "Paper fallback batch",
                TimelineModel.ShortId(paperReference.EntryBatchId)));
            afterFacts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
            afterFacts.Add(Fact(
                "Paper row",
                TimelineModel.ShortId(paperReference.EntryBatchRowId)));
            afterFacts.Add(Fact(
                "Line number",
                Presentation.Number(paperReference.LineNumber)));
            afterFacts.Add(Fact(
                "Event type",
                Presentation.Text(
                    "PaperFallback.EventType.correction_or_cancellation")));
            afterFacts.Add(Fact("Explanation", paperReference.Explanation));
        }
        AddMembershipFacts(afterFacts, afterMembership);

        return CreateExplanation(membershipId is null ? "VisitCanceled.WithoutMembership" : "VisitCanceled.WithMembership",
            "visit-canceled",
            beforeFacts,
            afterFacts,
            ChangedFields: membershipId is null
                ? Presentation.Changed("VisitStatus")
                : JoinChanged("VisitStatus", "ConsumptionStatus", "MembershipState"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateFreezeCancellation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var originalElement = RequireObject(before, "freeze");
        var canceledElement = RequireObject(after, "freeze");
        var cancellation = RequireObject(after, "cancellation");
        var original = ReadFreeze(originalElement);
        var canceled = ReadFreeze(canceledElement);
        var beforeMembership = ReadFreezeMembershipState(before);
        var afterMembership = ReadFreezeMembershipState(after);
        var originalEntryOrigin = RequireString(originalElement, "entryOrigin");
        var originalEntryBatchId = RequireNullableGuid(originalElement, "entryBatchId");
        var cancellationEntryOrigin = RequireString(cancellation, "entryOrigin");
        var cancellationEntryBatchId = RequireNullableGuid(cancellation, "entryBatchId");
        var cancellationRecordedAt = RequireTimestamp(cancellation, "recordedAt");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            cancellationEntryBatchId,
            "Freeze cancellation");

        _ = RequireTimestamp(originalElement, "occurredAt");
        _ = RequireTimestamp(originalElement, "recordedAt");
        ValidateEntryBatch(originalEntryOrigin, originalEntryBatchId);
        ValidateEntryBatch(cancellationEntryOrigin, cancellationEntryBatchId);

        if (original.FreezeId != entry.EntityId
            || canceled.FreezeId != original.FreezeId
            || RequireGuid(cancellation, "freezeId") != original.FreezeId
            || RequireGuid(cancellation, "cancellationId") == Guid.Empty
            || original.Status != "active"
            || canceled.Status != "canceled"
            || (canceled with { Status = "active" }) != original
            || beforeMembership.MembershipId != original.MembershipId
            || afterMembership.MembershipId != original.MembershipId
            || beforeMembership.ClientId != original.ClientId
            || afterMembership.ClientId != original.ClientId
            || beforeMembership.RemainingVisits != afterMembership.RemainingVisits
            || beforeMembership.NegativeBalance != afterMembership.NegativeBalance
            || afterMembership.ExtensionDays > beforeMembership.ExtensionDays
            || afterMembership.EffectiveEndDate > beforeMembership.EffectiveEndDate
            || RequireString(cancellation, "reason") != entry.Reason
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                RequireTimestamp(cancellation, "occurredAt"),
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                cancellationRecordedAt,
                entry.RecordedAt)
            || cancellationEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || RequireBoolean(cancellation, "changedAfterClose")
                != entry.ChangedAfterClose)
        {
            throw new JsonException("Freeze cancellation summary is inconsistent.");
        }

        var membershipStateChanged =
            beforeMembership.ExtensionDays != afterMembership.ExtensionDays
            || beforeMembership.EffectiveEndDate != afterMembership.EffectiveEndDate;

        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Original fact", Presentation.Value("Preserved")),
            Fact("Status", Presentation.Status("Canceled")),
            Fact(
                "Extension days",
                Presentation.Days(afterMembership.ExtensionDays)),
            Fact("Effective end", DateLabel(afterMembership.EffectiveEndDate)),
            Fact(
                "Cancellation recorded",
                TimelineModel.TimestampLabel(cancellationRecordedAt)),
        ];
        if (paperReference is not null)
        {
            afterFacts.Add(Fact(
                "Paper fallback batch",
                TimelineModel.ShortId(paperReference.EntryBatchId)));
            afterFacts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
            afterFacts.Add(Fact(
                "Paper row",
                TimelineModel.ShortId(paperReference.EntryBatchRowId)));
            afterFacts.Add(Fact(
                "Line number",
                Presentation.Number(paperReference.LineNumber)));
            afterFacts.Add(Fact(
                "Event type",
                Presentation.Text(
                    "PaperFallback.EventType.correction_or_cancellation")));
            afterFacts.Add(Fact("Explanation", paperReference.Explanation));
        }

        return CreateExplanation("FreezeCanceled",
            "freeze-canceled",
            [
                Fact("Period", FreezeRangeLabel(original)),
                Fact(
                    "Inclusive days",
                    Presentation.Days(original.InclusiveDays)),
                Fact("Freeze reason", original.Reason),
                Fact("Status", Presentation.Status("Active")),
                Fact("Original entry origin", StoredEntryOriginLabel(originalEntryOrigin)),
                Fact(
                    "Extension days",
                    Presentation.Days(beforeMembership.ExtensionDays)),
                Fact("Effective end", DateLabel(beforeMembership.EffectiveEndDate)),
            ],
            afterFacts,
            ChangedFields: membershipStateChanged
                ? JoinChanged("FreezeStatus", "MembershipExtensionState")
                : Presentation.Changed("FreezeStatus"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateFreezeAddition(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var relatedClientId = RequireGuid(related, "clientId");
        var relatedMembershipId = RequireGuid(related, "membershipId");
        var beforeMembership = ReadFreezeMembershipState(before);
        var freezeElement = RequireObject(after, "freeze");
        var freeze = ReadFreeze(freezeElement);
        var afterMembership = ReadFreezeMembershipState(after);
        var occurredAt = RequireTimestamp(freezeElement, "occurredAt");
        var recordedAt = RequireTimestamp(freezeElement, "recordedAt");
        var entryOrigin = RequireString(freezeElement, "entryOrigin");
        var entryBatchId = RequireNullableGuid(freezeElement, "entryBatchId");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            entryBatchId,
            "Freeze");
        ValidateEntryBatch(entryOrigin, entryBatchId);

        if (freeze.FreezeId != entry.EntityId
            || freeze.ClientId != relatedClientId
            || freeze.MembershipId != relatedMembershipId
            || beforeMembership.MembershipId != freeze.MembershipId
            || afterMembership.MembershipId != freeze.MembershipId
            || beforeMembership.ClientId != freeze.ClientId
            || afterMembership.ClientId != freeze.ClientId
            || beforeMembership.RemainingVisits != afterMembership.RemainingVisits
            || beforeMembership.NegativeBalance != afterMembership.NegativeBalance
            || afterMembership.ExtensionDays < beforeMembership.ExtensionDays
            || afterMembership.EffectiveEndDate < beforeMembership.EffectiveEndDate
            || freeze.Reason != entry.Reason
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                occurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                recordedAt,
                entry.RecordedAt)
            || entryOrigin != EntryOriginValue(entry.EntryOrigin)
            || freeze.Status != "active")
        {
            throw new JsonException("Freeze addition summary is inconsistent.");
        }

        var membershipStateChanged =
            beforeMembership.ExtensionDays != afterMembership.ExtensionDays
            || beforeMembership.EffectiveEndDate != afterMembership.EffectiveEndDate;

        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Freeze", TimelineModel.ShortId(freeze.FreezeId)),
            Fact("Client", TimelineModel.ShortId(freeze.ClientId)),
            Fact("Membership", TimelineModel.ShortId(freeze.MembershipId)),
            Fact("Period", FreezeRangeLabel(freeze)),
            Fact(
                "Inclusive days",
                Presentation.Days(freeze.InclusiveDays)),
            Fact("Freeze reason", freeze.Reason),
            Fact("Occurred", TimelineModel.TimestampLabel(occurredAt)),
            Fact("Entry origin", StoredEntryOriginLabel(entryOrigin)),
            Fact(
                "Entry batch",
                entryBatchId is { } batchId
                    ? TimelineModel.ShortId(batchId)
                    : Presentation.Value("None")),
            Fact("Source status", StatusLabel(freeze.Status)),
        ];
        if (paperReference is not null)
        {
            afterFacts.Add(Fact("Paper sheet", paperReference.PaperSheetNumber));
            afterFacts.Add(Fact(
                "Paper row",
                TimelineModel.ShortId(paperReference.EntryBatchRowId)));
            afterFacts.Add(Fact(
                "Line number",
                Presentation.Number(paperReference.LineNumber)));
            afterFacts.Add(Fact(
                "Event type",
                Presentation.Text("PaperFallback.EventType.freeze")));
            afterFacts.Add(Fact("Explanation", paperReference.Explanation));
        }

        afterFacts.Add(Fact(
            "Extension days",
            Presentation.Days(afterMembership.ExtensionDays)));
        afterFacts.Add(Fact(
            "Effective end",
            DateLabel(afterMembership.EffectiveEndDate)));

        return CreateExplanation("FreezeAdded",
            "freeze-added",
            [
                Fact("Freeze", Presentation.Value("NotPresent")),
                Fact("Membership", TimelineModel.ShortId(freeze.MembershipId)),
                Fact(
                    "Extension days",
                    Presentation.Days(beforeMembership.ExtensionDays)),
                Fact("Effective end", DateLabel(beforeMembership.EffectiveEndDate)),
            ],
            afterFacts,
            ChangedFields: membershipStateChanged
                ? JoinChanged("FreezeSource", "MembershipExtensionState")
                : Presentation.Changed("FreezeSource"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateNegativeClosurePaymentLifecycle(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after,
        bool isCancellation)
    {
        var original = ReadNegativeClosureLifecyclePayment(
            RequireObject(before, "payment"));
        var updated = ReadNegativeClosureLifecyclePayment(
            RequireObject(after, "payment"));
        var replacementPaymentId = RequireNullableGuid(
            after,
            "replacementPaymentId");
        var summarizedReplacementPaymentAuditId = RequireNullableGuid(
            after,
            "replacementPaymentAuditEntryId");
        var replacementPaymentElement = RequireNullableObject(
            after,
            "replacementPayment");
        var replacementPayment = replacementPaymentElement is null
            ? null
            : ReadNegativeClosureLifecyclePayment(
                replacementPaymentElement.Value);
        var replacementCoverageWitnessElement = RequireNullableObject(
            after,
            "replacementCoverageWitness");
        IReadOnlyList<NegativeClosureAuditLineSnapshot> replacementLines = [];
        decimal? expectedReplacementAmount = null;
        string? expectedReplacementCurrency = null;
        Guid? witnessedPaymentAuditId = null;
        string? witnessedPaymentAuditAction = null;
        string? witnessedPaymentAuditEntityType = null;
        Guid? witnessedPaymentAuditEntityId = null;
        if (replacementCoverageWitnessElement is { } replacementCoverageWitness)
        {
            replacementLines = ReadNegativeClosureLines(
                replacementCoverageWitness);
            expectedReplacementAmount = RequireDecimal(
                replacementCoverageWitness,
                "expectedAmount");
            expectedReplacementCurrency = RequireString(
                replacementCoverageWitness,
                "expectedCurrency");
            var paymentAudit = RequireObject(
                replacementCoverageWitness,
                "paymentAudit");
            witnessedPaymentAuditId = RequireGuid(paymentAudit, "auditEntryId");
            witnessedPaymentAuditAction = RequireString(paymentAudit, "actionType");
            witnessedPaymentAuditEntityType = RequireString(
                paymentAudit,
                "entityType");
            witnessedPaymentAuditEntityId = RequireGuid(paymentAudit, "entityId");
        }
        var correction = RequireObject(after, "correction");
        var correctionId = RequireGuid(correction, "correctionId");
        var correctionOccurredAt = RequireTimestamp(correction, "occurredAt");
        var correctionRecordedAt = RequireTimestamp(correction, "recordedAt");
        var correctionOrigin = RequireString(correction, "entryOrigin");
        var correctionBatchId = RequireNullableGuid(
            correction,
            "entryBatchId");
        var originalClosureId = RequireGuid(
            related,
            "originalNegativeClosureId");
        var replacementClosureId = RequireNullableGuid(
            related,
            "replacementNegativeClosureId");
        var relatedReplacementPaymentId = RequireNullableGuid(
            related,
            "replacementPaymentId");
        var replacementPaymentAuditId = RequireNullableGuid(
            related,
            "replacementPaymentAuditEntryId");
        var paper = ReadPaperReference(
            related,
            entry.EntryOrigin,
            correctionBatchId,
            "Negative closure Payment correction");
        ValidateEntryBatch(correctionOrigin, correctionBatchId);

        var expectedStatus = isCancellation ? "canceled" : "replaced";
        if (original.PaymentId != entry.EntityId
            || original.ClientId != RequireGuid(related, "clientId")
            || original.NegativeClosureId != originalClosureId
            || original.PaymentContext != "negative_closure"
            || original.Method != "cash"
            || original.Status != "active"
            || updated != original with { Status = expectedStatus }
            || RequireGuid(related, "correctionId") != correctionId
            || RequireGuid(after, "coverageCorrectionId") != correctionId
            || replacementPaymentId != relatedReplacementPaymentId
            || summarizedReplacementPaymentAuditId
                != replacementPaymentAuditId
            || !RequireBoolean(after, "noRefundOrDeltaCalculated")
            || RequireBoolean(after, "changedAfterClose")
                != entry.ChangedAfterClose
            || RequireBoolean(correction, "changedAfterClose")
                != entry.ChangedAfterClose
            || RequireString(correction, "reason") != entry.Reason
            || correctionOrigin != EntryOriginValue(entry.EntryOrigin)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionOccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionRecordedAt,
                entry.RecordedAt)
            || (isCancellation
                && (replacementClosureId is not null
                    || replacementPaymentId is not null
                    || replacementPayment is not null
                    || replacementPaymentAuditId is not null
                    || replacementCoverageWitnessElement is not null))
            || (!isCancellation
                && (replacementClosureId is null
                    || replacementPaymentId is null
                    || replacementPayment is null
                    || replacementPaymentAuditId is null
                    || replacementCoverageWitnessElement is null
                    || expectedReplacementAmount is null
                    || expectedReplacementAmount <= 0
                    || expectedReplacementCurrency is null
                    || replacementPayment.Amount != expectedReplacementAmount
                    || replacementLines.Sum(line => line.LineTotal)
                        != expectedReplacementAmount
                    || replacementLines.Any(
                        line => line.Currency != expectedReplacementCurrency)
                    || replacementPayment.Currency
                        != expectedReplacementCurrency
                    || witnessedPaymentAuditId != replacementPaymentAuditId
                    || witnessedPaymentAuditAction != "payment.created"
                    || witnessedPaymentAuditEntityType != "payment"
                    || witnessedPaymentAuditEntityId != replacementPaymentId
                    || replacementPayment.PaymentId
                        != replacementPaymentId
                    || replacementPayment.ClientId != original.ClientId
                    || replacementPayment.NegativeClosureId
                        != replacementClosureId
                    || replacementPayment.PaymentContext
                        != "negative_closure"
                    || replacementPayment.Method != "cash"
                    || replacementPayment.Status != "active"
                    || replacementPayment.EntryOrigin != correctionOrigin
                    || replacementPayment.EntryBatchId != correctionBatchId
                    || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                        replacementPayment.OccurredAt,
                        correctionOccurredAt)
                    || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                        replacementPayment.RecordedAt,
                        correctionRecordedAt))))
        {
            throw new JsonException(
                "Negative closure Payment lifecycle summary is inconsistent.");
        }

        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Payment record", TimelineModel.ShortId(original.PaymentId)),
            Fact("Correction", TimelineModel.ShortId(correctionId)),
            Fact("Negative closure", TimelineModel.ShortId(originalClosureId)),
            Fact("Original status", Presentation.Status(expectedStatus)),
        };
        if (replacementPaymentId is { } replacementId)
        {
            afterFacts.Add(Fact(
                "Replacement",
                TimelineModel.ShortId(replacementId)));
        }

        AddPaperReferenceFacts(
            afterFacts,
            paper,
            PaperFallbackEventType.CorrectionOrCancellation);
        return CreateExplanation(
            isCancellation
                ? "NegativeClosurePaymentCanceled"
                : "NegativeClosurePaymentCorrected",
            isCancellation ? "payment-canceled" : "payment-corrected",
            [
                Fact("Payment record", TimelineModel.ShortId(original.PaymentId)),
                Fact("Amount", MoneyLabel(original.Amount, original.Currency)),
                Fact("Status", Presentation.Status("Active")),
                Fact("Negative closure", TimelineModel.ShortId(originalClosureId)),
            ],
            afterFacts,
            JoinChanged("PaymentStatus", "NegativeVisitCoverage"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaymentCorrection(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var original = ReadPayment(RequireObject(before, "payment"));
        var preservedOriginal = ReadPayment(RequireObject(after, "originalPayment"));
        var replacement = ReadPayment(RequireObject(after, "replacementPayment"));
        var correction = RequireObject(after, "correction");
        var correctionId = RequireGuid(correction, "correctionId");
        var correctionReason = RequireString(correction, "reason");
        var correctionOccurredAt = RequireTimestamp(correction, "occurredAt");
        var correctionRecordedAt = RequireTimestamp(correction, "recordedAt");
        var correctionEntryOrigin = RequireString(correction, "entryOrigin");
        var correctionEntryBatchId = RequireNullableGuid(
            correction,
            "entryBatchId");
        var correctionChangedAfterClose = RequireBoolean(
            correction,
            "changedAfterClose");
        var changedFields = RequireStringArray(correction, "changedFields");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            correctionEntryBatchId,
            "Payment correction");
        ValidateEntryBatch(correctionEntryOrigin, correctionEntryBatchId);

        if (original.PaymentId != entry.EntityId
            || original.ClientId != RequireGuid(related, "clientId")
            || preservedOriginal.PaymentId != original.PaymentId
            || RequireGuid(correction, "originalPaymentId") != original.PaymentId
            || RequireGuid(correction, "replacementPaymentId") != replacement.PaymentId
            || correctionId == Guid.Empty
            || RequireGuid(related, "originalPaymentId") != original.PaymentId
            || RequireNullableGuid(related, "originalMembershipId")
                != original.MembershipId
            || RequireGuid(related, "replacementPaymentId") != replacement.PaymentId
            || RequireNullableGuid(related, "replacementMembershipId")
                != replacement.MembershipId
            || RequireGuid(related, "correctionId") != correctionId
            || replacement.PaymentId == original.PaymentId
            || replacement.ClientId != original.ClientId
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                replacement.RecordedAt,
                correctionRecordedAt)
            || replacement.EntryOrigin != correctionEntryOrigin
            || replacement.EntryBatchId != correctionEntryBatchId
            || correctionReason != entry.Reason
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionOccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionRecordedAt,
                entry.RecordedAt)
            || correctionEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || correctionChangedAfterClose != entry.ChangedAfterClose
            || original.Status != "active"
            || preservedOriginal.Status != "replaced"
            || (preservedOriginal with { Status = "active" }) != original
            || replacement.Status != "active"
            || changedFields.Count == 0)
        {
            throw new JsonException("Payment correction summary identity is inconsistent.");
        }

        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Original status", Presentation.Status("Replaced")),
        };
        afterFacts.AddRange(PaymentFacts(replacement));
        AddPaperReferenceFacts(
            afterFacts,
            paperReference,
            PaperFallbackEventType.CorrectionOrCancellation);
        return CreateExplanation("PaymentCorrected",
            "payment-corrected",
            PaymentFacts(original),
            afterFacts,
            string.Join(", ", changedFields.Select(ChangedFieldLabel)),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaymentCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any())
        {
            throw new JsonException(
                "A Payment creation cannot have a pre-existing Payment summary.");
        }

        var relatedClientId = RequireGuid(related, "clientId");
        var relatedMembershipId = RequireNullableGuid(related, "membershipId");
        var relatedNegativeClosureId = ReadOptionalGuid(
            related,
            "negativeClosureId");
        var coverageCorrectionId = ReadOptionalGuid(
            related,
            "coverageCorrectionId");
        var saleCorrectionId = ReadOptionalGuid(
            related,
            "saleCorrectionId");
        var payment = ReadCreatedPayment(RequireObject(after, "payment"));
        ValidateEntryBatch(payment.EntryOrigin, payment.EntryBatchId);
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            payment.EntryBatchId,
            "Payment");

        if (payment.PaymentId != entry.EntityId
            || payment.ClientId != relatedClientId
            || payment.MembershipId != relatedMembershipId
            || payment.NegativeClosureId != relatedNegativeClosureId
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                payment.OccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                payment.RecordedAt,
                entry.RecordedAt)
            || payment.EntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || payment.Comment != entry.Comment
            || payment.Method != "cash"
            || payment.Status != "active")
        {
            throw new JsonException("Created Payment summary is inconsistent.");
        }

        if (payment.PaymentContext == "negative_closure")
        {
            var explanation = RequireObject(after, "explanation");
            var explanationCorrectionId = ReadOptionalGuid(
                explanation,
                "coverageCorrectionId");
            if (payment.MembershipId is not null
                || payment.NegativeClosureId is null
                || RequireString(explanation, "kind")
                    != "negative_visit_one_off_closure"
                || RequireGuid(explanation, "negativeClosureId")
                    != payment.NegativeClosureId
                || RequireBoolean(explanation, "isStandalonePayment")
                || coverageCorrectionId != explanationCorrectionId
                || coverageCorrectionId == Guid.Empty
                || saleCorrectionId is not null
                || (coverageCorrectionId is not null
                    && RequireBoolean(explanation, "changedAfterClose")
                        != entry.ChangedAfterClose))
            {
                throw new JsonException(
                    "Negative-closure Payment explanation is inconsistent.");
            }
        }
        else if (payment.NegativeClosureId is not null)
        {
            throw new JsonException(
                "Only a negative-closure Payment can reference a closure.");
        }

        if (payment.PaymentContext == "membership_sale")
        {
            if (coverageCorrectionId is not null
                || saleCorrectionId == Guid.Empty)
            {
                throw new JsonException(
                    "Membership-sale Payment correction provenance is inconsistent.");
            }
        }
        else if (saleCorrectionId is not null)
        {
            throw new JsonException(
                "Only a Membership-sale Payment can reference an issued-sale correction.");
        }

        var context = PaymentContextLabel(payment.PaymentContext);
        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Payment", TimelineModel.ShortId(payment.PaymentId)),
            Fact("Client", TimelineModel.ShortId(payment.ClientId)),
            Fact("Amount", MoneyLabel(payment.Amount, payment.Currency)),
            Fact("Method", PaymentMethodLabel(payment.Method)),
            Fact("Context", context),
            Fact("Membership", OptionalIdLabel(payment.MembershipId)),
            Fact("Occurred", TimelineModel.TimestampLabel(payment.OccurredAt)),
            Fact("Status", Presentation.Status("Active")),
        };
        if (payment.NegativeClosureId is { } negativeClosureId)
        {
            afterFacts.Add(Fact(
                "Negative closure",
                TimelineModel.ShortId(negativeClosureId)));
        }
        var correctionId = coverageCorrectionId ?? saleCorrectionId;
        if (correctionId is { } sourceCorrectionId)
        {
            afterFacts.Add(Fact("Correction", TimelineModel.ShortId(sourceCorrectionId)));
        }
        AddPaperReferenceFacts(
            afterFacts,
            paperReference,
            correctionId is null
                ? PaymentPaperEventType(payment.PaymentContext)
                : PaperFallbackEventType.CorrectionOrCancellation);
        return CreateExplanation("PaymentCreated",
            "payment-created",
            [
                Fact("Payment", Presentation.Value("NotPresent")),
            ],
            afterFacts,
            ChangedFields: Presentation.Changed("Payment"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaymentCancellation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var original = ReadPayment(RequireObject(before, "payment"));
        var canceled = ReadPayment(RequireObject(after, "payment"));
        var cancellation = RequireObject(after, "cancellation");
        var cancellationId = RequireGuid(cancellation, "cancellationId");
        var cancellationReason = RequireString(cancellation, "reason");
        var cancellationOccurredAt = RequireTimestamp(cancellation, "occurredAt");
        var cancellationRecordedAt = RequireTimestamp(cancellation, "recordedAt");
        var cancellationEntryOrigin = RequireString(cancellation, "entryOrigin");
        var cancellationEntryBatchId = RequireNullableGuid(
            cancellation,
            "entryBatchId");
        var cancellationChangedAfterClose = RequireBoolean(
            cancellation,
            "changedAfterClose");
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            cancellationEntryBatchId,
            "Payment cancellation");
        ValidateEntryBatch(cancellationEntryOrigin, cancellationEntryBatchId);

        if (original.PaymentId != entry.EntityId
            || original.ClientId != RequireGuid(related, "clientId")
            || canceled.PaymentId != original.PaymentId
            || RequireGuid(cancellation, "paymentId") != original.PaymentId
            || cancellationId == Guid.Empty
            || RequireGuid(related, "paymentId") != original.PaymentId
            || RequireNullableGuid(related, "membershipId") != original.MembershipId
            || RequireGuid(related, "cancellationId") != cancellationId
            || cancellationReason != entry.Reason
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                cancellationOccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                cancellationRecordedAt,
                entry.RecordedAt)
            || cancellationEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || cancellationChangedAfterClose != entry.ChangedAfterClose
            || original.Status != "active"
            || canceled.Status != "canceled"
            || (canceled with { Status = "active" }) != original)
        {
            throw new JsonException("Payment cancellation summary identity is inconsistent.");
        }

        var afterFacts = PaymentFacts(canceled).ToList();
        AddPaperReferenceFacts(
            afterFacts,
            paperReference,
            PaperFallbackEventType.CorrectionOrCancellation);
        return CreateExplanation("PaymentCanceled",
            "payment-canceled",
            PaymentFacts(original),
            afterFacts,
            ChangedFields: Presentation.Changed("PaymentStatus"),
            IsAvailable: true);
    }

    private static PaperFallbackEventType PaymentPaperEventType(
        string paymentContext) => paymentContext switch
        {
            "membership_sale" => PaperFallbackEventType.MembershipSale,
            "negative_closure" => PaperFallbackEventType.NegativeCoverage,
            "one_off" or "trial" or "other" => PaperFallbackEventType.Payment,
            _ => throw new JsonException(
                $"Unsupported Payment context '{paymentContext}'."),
        };

    private AuditEntryExplanationViewModel CreateMembershipIssue(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any())
        {
            throw new JsonException(
                "A Membership issue cannot have a pre-existing Membership summary.");
        }

        var relatedClientId = RequireGuid(related, "clientId");
        var relatedMembershipTypeId = RequireGuid(related, "membershipTypeId");
        var relatedPaymentId = RequireNullableGuid(related, "paymentId");
        var issue = ReadMembershipIssue(after);
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            issue.EntryBatchId,
            "Membership issue");
        ValidateEntryBatch(issue.EntryOrigin, issue.EntryBatchId);

        if (issue.MembershipId != entry.EntityId
            || issue.ClientId != relatedClientId
            || issue.MembershipTypeId != relatedMembershipTypeId
            || issue.Payment?.PaymentId != relatedPaymentId
            || issue.EntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || issue.Comment != entry.Comment
            || issue.StartDate > issue.BaseEndDate
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                issue.IssuedAt,
                entry.RecordedAt)
            || issue.Status != "active"
            || (issue.NegativeHandlingDecision is not null
                && issue.NegativeCoveragePolicy is not null)
            || (issue.NegativeCoveragePolicy is not null
                && issue.NegativeCoveragePolicy != "automatic_oldest_first")
            || (issue.NegativeCoveragePolicy is not null
                && issue.ExistingNegativeState is null)
            || (issue.NegativeCoveragePolicy is null
                && (issue.NegativeHandlingDecision is not null)
                    != (issue.ExistingNegativeState is not null))
            || (issue.NegativeCoverage is not null
                && issue.NegativeCoveragePolicy != "automatic_oldest_first"))
        {
            throw new JsonException("Membership issue summary identity is inconsistent.");
        }

        var negativeHandling = issue.NegativeCoveragePolicy is not null
            ? MembershipNegativeCoveragePolicyLabel(issue.NegativeCoveragePolicy)
            : MembershipNegativeHandlingLabel(issue.NegativeHandlingDecision);
        List<AuditEntryExplanationFactViewModel> beforeFacts =
        [
            Fact("Membership", Presentation.Value("NotPresent")),
            Fact(
                "Existing negative balance",
                issue.ExistingNegativeState is null
                    ? Presentation.Value("None")
                    : Presentation.Number(
                        issue.ExistingNegativeState.NegativeBalance)),
        ];
        if (issue.ExistingNegativeState is { FirstNegativeVisitDate: { } } existingNegativeState)
        {
            beforeFacts.Add(Fact(
                "First negative visit date",
                DateLabel(existingNegativeState.FirstNegativeVisitDate.Value)));
        }

        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Membership", TimelineModel.ShortId(issue.MembershipId)),
            Fact("Client", TimelineModel.ShortId(issue.ClientId)),
            Fact("Membership type", TimelineModel.ShortId(issue.MembershipTypeId)),
            Fact("Type snapshot", issue.Snapshot.TypeName),
            Fact(
                "Duration",
                Presentation.Days(issue.Snapshot.DurationDays)),
            Fact(
                "Visit limit",
                Presentation.Number(issue.Snapshot.VisitsLimit)),
            Fact(
                "Snapshot price",
                MoneyLabel(
                    issue.Snapshot.PriceAmount,
                    issue.Snapshot.PriceCurrency)),
            Fact("Start date", DateLabel(issue.StartDate)),
            Fact("Base end date", DateLabel(issue.BaseEndDate)),
            Fact("Status", MembershipStatusLabel(issue.Status)),
            Fact(
                "Initial counted visits",
                Presentation.Number(issue.InitialState.CountedVisits)),
            Fact(
                "Initial remaining visits",
                Presentation.Number(issue.InitialState.RemainingVisits)),
            Fact(
                "Initial negative balance",
                Presentation.Number(issue.InitialState.NegativeBalance)),
            Fact(
                "Initial extension days",
                Presentation.Days(issue.InitialState.ExtensionDays)),
            Fact(
                "Initial effective end date",
                DateLabel(issue.InitialState.EffectiveEndDate)),
        ];
        if (issue.ExistingNegativeState is not null || issue.NegativeHandlingDecision is not null)
        {
            afterFacts.Add(Fact("Negative handling", negativeHandling));
        }
        if (issue.InitialState.FirstNegativeVisitDate is { } firstNegativeVisitDate)
        {
            afterFacts.Add(Fact(
                "Initial first negative visit date",
                DateLabel(firstNegativeVisitDate)));
        }

        if (issue.NegativeCoverage is { } negativeCoverage)
        {
            afterFacts.Add(Fact("Covered visits", Presentation.Number(negativeCoverage.Count)));
            afterFacts.Add(Fact(
                "Remaining negative balance",
                Presentation.Number(negativeCoverage.RemainingExistingNegativeBalance)));
            afterFacts.Add(Fact("Forced coverage start", DateLabel(negativeCoverage.ForcedStartDate)));
        }

        if (issue.Payment is null)
        {
            afterFacts.Add(Fact("Payment", Presentation.Value("None")));
        }

        else
        {
            afterFacts.Add(Fact(
                "Payment",
                $"{MoneyLabel(issue.Payment.Amount, issue.Payment.Currency)} / " +
                PaymentMethodLabel(issue.Payment.Method)));
            afterFacts.Add(Fact(
                "Payment record",
                TimelineModel.ShortId(issue.Payment.PaymentId)));
        }

        AddPaperReferenceFacts(
            afterFacts,
            paperReference,
            PaperFallbackEventType.MembershipSale);

        return CreateExplanation("MembershipIssued",
            "membership-issued",
            beforeFacts,
            afterFacts,
            ChangedFields: Presentation.Changed("IssuedMembership"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreateMembershipSaleCorrection(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after,
        bool isCancellation)
    {
        var clientId = RequireGuid(related, "clientId");
        var saleCorrectionId = RequireGuid(related, "saleCorrectionId");
        var originalMembershipId = RequireGuid(related, "originalMembershipId");
        var originalPaymentId = RequireGuid(related, "originalPaymentId");
        var replacementMembershipId = RequireNullableGuid(
            related,
            "replacementMembershipId");
        var replacementPaymentId = RequireNullableGuid(
            related,
            "replacementPaymentId");
        _ = RequireGuid(related, "paymentLifecycleAuditEntryId");
        var replacementPaymentCreatedAuditEntryId = RequireNullableGuid(
            related,
            "replacementPaymentCreatedAuditEntryId");

        var originalMembership = ReadIssuedSaleMembership(
            RequireObject(before, "originalMembership"));
        var originalPayment = ReadIssuedSalePayment(
            RequireObject(before, "originalPayment"));
        _ = RequireObject(before, "dependencies");

        var correction = RequireObject(after, "correction");
        var afterOriginalMembership = ReadIssuedSaleMembership(
            RequireObject(after, "originalMembership"));
        var afterOriginalPayment = ReadIssuedSalePayment(
            RequireObject(after, "originalPayment"));
        var replacementMembershipElement = RequireNullableObject(
            after,
            "replacementMembership");
        var replacementMembership = replacementMembershipElement is null
            ? null
            : ReadIssuedSaleMembership(replacementMembershipElement.Value);
        var afterReplacementPaymentId = RequireNullableGuid(
            after,
            "replacementPaymentId");

        var expectedMode = isCancellation ? "cancel" : "replace";
        var expectedMembershipStatus = isCancellation ? "canceled" : "corrected";
        var expectedPaymentStatus = isCancellation ? "canceled" : "replaced";
        var correctionOccurredAt = RequireTimestamp(correction, "occurredAt");
        var correctionRecordedAt = RequireTimestamp(correction, "recordedAt");
        var correctionEntryOrigin = RequireString(correction, "entryOrigin");
        var correctionEntryBatchId = RequireNullableGuid(
            correction,
            "entryBatchId");
        var correctionReason = RequireString(correction, "reason");
        ValidateEntryBatch(correctionEntryOrigin, correctionEntryBatchId);
        var paperReference = ReadPaperReference(
            related,
            entry.EntryOrigin,
            correctionEntryBatchId,
            "issued Membership sale correction");

        ValidateIssuedSale(
            originalMembership,
            originalPayment,
            expectedMembershipStatus: "active",
            expectedPaymentStatus: "active");

        if (entry.EntityId == Guid.Empty
            || entry.EntityId != originalMembershipId
            || originalMembership.MembershipId != originalMembershipId
            || originalPayment.PaymentId != originalPaymentId
            || originalMembership.ClientId != clientId
            || RequireGuid(correction, "saleCorrectionId") != saleCorrectionId
            || RequireString(correction, "mode") != expectedMode
            || correctionReason != entry.Reason
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionOccurredAt,
                entry.OccurredAt)
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                correctionRecordedAt,
                entry.RecordedAt)
            || correctionEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || RequireString(correction, "status") != "active"
            || afterOriginalMembership.Status != expectedMembershipStatus
            || afterOriginalPayment.Status != expectedPaymentStatus
            || (afterOriginalMembership with { Status = "active" })
                != originalMembership
            || (afterOriginalPayment with { Status = "active" })
                != originalPayment
            || afterReplacementPaymentId != replacementPaymentId)
        {
            throw new JsonException(
                "Issued Membership sale correction summary is inconsistent.");
        }

        if (isCancellation)
        {
            if (replacementMembershipId is not null
                || replacementPaymentId is not null
                || replacementPaymentCreatedAuditEntryId is not null
                || replacementMembership is not null)
            {
                throw new JsonException(
                    "Canceled Membership sale cannot contain a replacement.");
            }
        }
        else
        {
            if (replacementMembershipId is null
                || replacementPaymentId is null
                || replacementPaymentCreatedAuditEntryId is null
                || replacementMembership is null
                || replacementMembership.MembershipId != replacementMembershipId
                || replacementMembership.MembershipId == originalMembershipId
                || replacementMembership.ClientId != clientId
                || replacementPaymentId == originalPaymentId
                || replacementMembership.Status != "active"
                || replacementMembership.EntryOrigin != correctionEntryOrigin
                || replacementMembership.EntryBatchId != correctionEntryBatchId
                || replacementMembership.Comment != entry.Comment
                || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                    replacementMembership.IssuedAt,
                    correctionRecordedAt))
            {
                throw new JsonException(
                    "Replacement Membership sale summary is inconsistent.");
            }

            ValidateIssuedMembership(replacementMembership);
        }

        List<AuditEntryExplanationFactViewModel> beforeFacts =
        [
            .. IssuedSaleMembershipFacts(originalMembership),
            Fact("Payment record", TimelineModel.ShortId(originalPayment.PaymentId)),
            Fact("Amount", MoneyLabel(originalPayment.Amount, originalPayment.Currency)),
            Fact("Occurred", TimelineModel.TimestampLabel(originalPayment.OccurredAt)),
            Fact("Source status", StatusLabel(originalPayment.Status)),
        ];
        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Membership", TimelineModel.ShortId(afterOriginalMembership.MembershipId)),
            Fact("Status", StatusLabel(afterOriginalMembership.Status)),
            Fact("Payment record", TimelineModel.ShortId(afterOriginalPayment.PaymentId)),
            Fact("Source status", StatusLabel(afterOriginalPayment.Status)),
            Fact("Reason comment", correctionReason),
            Fact("Occurred", TimelineModel.TimestampLabel(correctionOccurredAt)),
            Fact("Entry origin", StoredEntryOriginLabel(correctionEntryOrigin)),
        ];

        if (replacementMembership is not null && replacementPaymentId is not null)
        {
            afterFacts.AddRange(IssuedSaleMembershipFacts(replacementMembership));
            afterFacts.Add(Fact(
                "Payment record",
                TimelineModel.ShortId(replacementPaymentId.Value)));
            afterFacts.Add(Fact(
                "Amount",
                MoneyLabel(
                    replacementMembership.PriceAmount,
                    replacementMembership.PriceCurrency)));
        }

        AddPaperReferenceFacts(
            afterFacts,
            paperReference,
            PaperFallbackEventType.CorrectionOrCancellation);

        return CreateExplanation(
            isCancellation ? "MembershipSaleCanceled" : "MembershipSaleReplaced",
            isCancellation
                ? "membership-sale-canceled"
                : "membership-sale-replaced",
            beforeFacts,
            afterFacts,
            ChangedFields: JoinChanged("Membership", "PaymentStatus"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel Unavailable(string kind)
    {
        return CreateExplanation("Unavailable",
            kind,
            BeforeFacts: [],
            AfterFacts: [],
            ChangedFields: null,
            IsAvailable: false,
            HasBeforeAfterSections: false);
    }

    private AuditEntryExplanationViewModel CreateExplanation(
        string resourceKey,
        string Kind,
        IReadOnlyList<AuditEntryExplanationFactViewModel> BeforeFacts,
        IReadOnlyList<AuditEntryExplanationFactViewModel> AfterFacts,
        string? ChangedFields,
        bool IsAvailable,
        bool HasBeforeAfterSections = true)
    {
        return new AuditEntryExplanationViewModel(
            Kind,
            Presentation.Explanation($"{resourceKey}.Title"),
            Presentation.Explanation($"{resourceKey}.Narrative"),
            HasBeforeAfterSections
                ? Presentation.Explanation($"{resourceKey}.Before")
                : string.Empty,
            HasBeforeAfterSections
                ? Presentation.Explanation($"{resourceKey}.After")
                : string.Empty,
            BeforeFacts,
            AfterFacts,
            ChangedFields,
            IsAvailable);
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> PaymentFacts(
        PaymentSnapshot payment)
    {
        return
        [
            Fact("Amount", MoneyLabel(payment.Amount, payment.Currency)),
            Fact("Occurred", TimelineModel.TimestampLabel(payment.OccurredAt)),
            Fact("Context", PaymentContextLabel(payment.PaymentContext)),
            Fact("Membership", OptionalIdLabel(payment.MembershipId)),
            Fact("Method", PaymentMethodLabel(payment.Method)),
            Fact("Status", StatusLabel(payment.Status)),
        ];
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> MembershipTypeFacts(
        MembershipTypeCatalogSnapshot membershipType)
    {
        return
        [
            Fact("Name", membershipType.Name),
            Fact(
                "Duration",
                Presentation.Days(membershipType.DurationDays)),
            Fact(
                "Visit limit",
                Presentation.Number(membershipType.VisitsLimit)),
            Fact(
                "Price",
                MoneyLabel(membershipType.PriceAmount, membershipType.PriceCurrency)),
            Fact(
                "Status",
                membershipType.IsActive
                    ? Presentation.Status("Active")
                    : Presentation.Status("Inactive")),
            Fact(
                "Catalog comment",
                membershipType.Comment ?? Presentation.Value("None")),
        ];
    }

    private static MembershipIssueSnapshot ReadMembershipIssue(JsonElement summary)
    {
        var snapshot = RequireObject(summary, "snapshot");
        var initialState = RequireObject(summary, "initialState");

        return new MembershipIssueSnapshot(
            RequireGuid(summary, "membershipId"),
            RequireGuid(summary, "clientId"),
            RequireGuid(summary, "membershipTypeId"),
            new MembershipIssueTermsSnapshot(
                RequireString(snapshot, "typeName"),
                RequirePositiveInt32(snapshot, "durationDays"),
                RequireNonNegativeInt32(snapshot, "visitsLimit"),
                RequireNonNegativeDecimal(snapshot, "priceAmount"),
                RequireString(snapshot, "priceCurrency")),
            RequireDateOnly(summary, "startDate"),
            RequireDateOnly(summary, "baseEndDate"),
            RequireTimestamp(summary, "issuedAt"),
            RequireString(summary, "status"),
            RequireString(summary, "entryOrigin"),
            RequireNullableGuid(summary, "entryBatchId"),
            RequireNullableString(summary, "comment"),
            ReadOptionalString(summary, "negativeHandlingDecision"),
            ReadOptionalString(summary, "negativeCoveragePolicy"),
            ReadExistingNegativeState(summary),
            ReadMembershipIssueNegativeCoverage(summary),
            ReadMembershipIssuePayment(summary),
            ReadMembershipIssueInitialState(initialState));
    }

    private static MembershipOpeningStateCreationSnapshot
        ReadMembershipOpeningStateCreation(JsonElement summary)
    {
        var openingAsOfDate = RequireDateOnly(summary, "openingAsOfDate");
        var declaredRemainingVisits = RequireInt32(summary, "declaredRemainingVisits");
        var declaredNegativeBalance = RequireNonNegativeInt32(
            summary,
            "declaredNegativeBalance");
        var knownEffectiveEndDate = RequireNullableDateOnly(
            summary,
            "knownEffectiveEndDate");
        var knownExtensionDays = RequireNullableNonNegativeInt32(
            summary,
            "knownExtensionDays");

        var recalculated = RequireObject(summary, "recalculatedState");
        return new MembershipOpeningStateCreationSnapshot(
            RequireGuid(summary, "openingStateId"),
            RequireGuid(summary, "membershipId"),
            RequireGuid(summary, "clientId"),
            openingAsOfDate,
            declaredRemainingVisits,
            declaredNegativeBalance,
            knownEffectiveEndDate,
            knownExtensionDays,
            RequireString(summary, "sourceReference"),
            RequireNullableGuid(summary, "entryBatchId"),
            RequireString(summary, "status"),
            new MembershipOpeningStateRecalculatedSnapshot(
                RequireInt32(recalculated, "remainingVisits"),
                RequireNonNegativeInt32(recalculated, "negativeBalance"),
                RequireDateOnly(recalculated, "effectiveEndDate"),
                RequireNonNegativeInt32(recalculated, "extensionDays"),
                RequirePositiveInt32(recalculated, "recalculationVersion")));
    }

    private static MembershipIssueExistingNegativeStateSnapshot?
        ReadExistingNegativeState(JsonElement summary)
    {
        var existingState = RequireNullableObject(summary, "existingNegativeState");
        return existingState is null
            ? null
            : new MembershipIssueExistingNegativeStateSnapshot(
                RequirePositiveInt32(existingState.Value, "negativeBalance"),
                RequireNullableDateOnly(existingState.Value, "firstNegativeVisitDate"),
                ReadOptionalInt32(existingState.Value, "openConcreteVisitCount") ?? 0,
                ReadOptionalInt32(existingState.Value, "unknownNegativeBalance") ?? 0);
    }

    private static MembershipIssueNegativeCoverageSnapshot?
        ReadMembershipIssueNegativeCoverage(JsonElement summary)
    {
        if (!summary.TryGetProperty("negativeCoverage", out var coverage)
            || coverage.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (coverage.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Membership issue negative coverage has an invalid shape.");
        }

        var visitIds = RequireGuidArray(coverage, "coveredVisitIds");
        var count = RequirePositiveInt32(coverage, "count");
        if (visitIds.Count != count || visitIds.Distinct().Count() != count)
        {
            throw new JsonException("Membership issue coverage visits are inconsistent.");
        }

        return new MembershipIssueNegativeCoverageSnapshot(
            RequireGuid(coverage, "negativeClosureId"),
            count,
            visitIds,
            RequireNonNegativeInt32(coverage, "remainingExistingNegativeBalance"),
            RequireDateOnly(coverage, "forcedStartDate"),
            RequireBoolean(coverage, "isAlreadyExpiredAtIssue"));
    }

    private static MembershipIssuePaymentSnapshot? ReadMembershipIssuePayment(
        JsonElement summary)
    {
        var payment = RequireNullableObject(summary, "payment");
        if (payment is null)
        {
            return null;
        }

        var result = new MembershipIssuePaymentSnapshot(
            RequireGuid(payment.Value, "paymentId"),
            RequireDecimal(payment.Value, "amount"),
            RequireString(payment.Value, "currency"),
            RequireString(payment.Value, "method"));
        _ = RequireGuid(payment.Value, "paymentAuditEntryId");
        var paymentContext = RequireString(payment.Value, "paymentContext");
        _ = RequireTimestamp(payment.Value, "occurredAt");

        if (result.Method != "cash" || paymentContext != "membership_sale")
        {
            throw new JsonException("Membership issue payment summary is inconsistent.");
        }

        return result;
    }

    private static MembershipIssueInitialStateSnapshot ReadMembershipIssueInitialState(
        JsonElement initialState)
    {
        var result = new MembershipIssueInitialStateSnapshot(
            RequireNonNegativeInt32(initialState, "countedVisits"),
            RequireInt32(initialState, "remainingVisits"),
            RequireNonNegativeInt32(initialState, "negativeBalance"),
            RequireNullableDateOnly(initialState, "firstNegativeVisitDate"),
            RequireNonNegativeInt32(initialState, "extensionDays"),
            RequireDateOnly(initialState, "effectiveEndDate"));
        _ = RequireNullableTimestamp(initialState, "lastCountedVisitAt");
        _ = RequirePositiveInt32(initialState, "recalculationVersion");
        return result;
    }

    private static MembershipTypeCatalogSnapshot ReadMembershipTypeCatalog(JsonElement summary)
    {
        var price = RequireObject(summary, "price");
        return new MembershipTypeCatalogSnapshot(
            RequireString(summary, "name"),
            RequirePositiveInt32(summary, "durationDays"),
            RequireNonNegativeInt32(summary, "visitsLimit"),
            RequireNonNegativeDecimal(price, "amount"),
            RequireString(price, "currency"),
            RequireBoolean(summary, "isActive"),
            RequireNullableString(summary, "comment"),
            RequireTimestamp(summary, "createdAt"),
            RequireTimestamp(summary, "updatedAt"),
            RequireNullableTimestamp(summary, "deactivatedAt"));
    }

    private IReadOnlyList<string> MembershipTypeChangedFields(
        MembershipTypeCatalogSnapshot original,
        MembershipTypeCatalogSnapshot updated)
    {
        var changedFields = new List<string>();
        if (original.Name != updated.Name)
        {
            changedFields.Add(Presentation.Changed("Name"));
        }

        if (original.DurationDays != updated.DurationDays)
        {
            changedFields.Add(Presentation.Changed("Duration"));
        }

        if (original.VisitsLimit != updated.VisitsLimit)
        {
            changedFields.Add(Presentation.Changed("VisitLimit"));
        }

        if (original.PriceAmount != updated.PriceAmount
            || original.PriceCurrency != updated.PriceCurrency)
        {
            changedFields.Add(Presentation.Changed("Price"));
        }

        if (original.Comment != updated.Comment)
        {
            changedFields.Add(Presentation.Changed("CatalogComment"));
        }

        return changedFields;
    }

    private static PaymentSnapshot ReadPayment(JsonElement payment)
    {
        return new PaymentSnapshot(
            RequireGuid(payment, "paymentId"),
            RequireGuid(payment, "clientId"),
            RequireNullableGuid(payment, "membershipId"),
            RequireDecimal(payment, "amount"),
            RequireString(payment, "currency"),
            RequireString(payment, "method"),
            RequireString(payment, "paymentContext"),
            RequireTimestamp(payment, "occurredAt"),
            RequireTimestamp(payment, "recordedAt"),
            RequireString(payment, "entryOrigin"),
            RequireNullableGuid(payment, "entryBatchId"),
            RequireNullableString(payment, "comment"),
            RequireString(payment, "status"));
    }

    private static IssuedSaleMembershipSnapshot ReadIssuedSaleMembership(
        JsonElement membership)
    {
        return new IssuedSaleMembershipSnapshot(
            RequireGuid(membership, "membershipId"),
            RequireGuid(membership, "clientId"),
            RequireGuid(membership, "membershipTypeId"),
            RequireString(membership, "typeNameSnapshot"),
            RequirePositiveInt32(membership, "durationDaysSnapshot"),
            RequireNonNegativeInt32(membership, "visitsLimitSnapshot"),
            RequireDecimal(membership, "priceAmountSnapshot"),
            RequireString(membership, "priceCurrencySnapshot"),
            RequireString(membership, "issuanceMode"),
            RequireDateOnly(membership, "startDate"),
            RequireDateOnly(membership, "baseEndDate"),
            RequireTimestamp(membership, "issuedAt"),
            RequireString(membership, "status"),
            RequireString(membership, "entryOrigin"),
            RequireNullableGuid(membership, "entryBatchId"),
            RequireNullableString(membership, "comment"));
    }

    private static IssuedSalePaymentSnapshot ReadIssuedSalePayment(
        JsonElement payment)
    {
        return new IssuedSalePaymentSnapshot(
            RequireGuid(payment, "paymentId"),
            RequireGuid(payment, "clientId"),
            RequireGuid(payment, "membershipId"),
            RequireDecimal(payment, "amount"),
            RequireString(payment, "currency"),
            RequireString(payment, "method"),
            RequireString(payment, "paymentContext"),
            RequireTimestamp(payment, "occurredAt"),
            RequireTimestamp(payment, "recordedAt"),
            RequireString(payment, "status"),
            RequireString(payment, "entryOrigin"),
            RequireNullableGuid(payment, "entryBatchId"),
            RequireNullableString(payment, "comment"));
    }

    private void ValidateIssuedSale(
        IssuedSaleMembershipSnapshot membership,
        IssuedSalePaymentSnapshot payment,
        string expectedMembershipStatus,
        string expectedPaymentStatus)
    {
        ValidateIssuedMembership(membership);
        ValidateEntryBatch(payment.EntryOrigin, payment.EntryBatchId);

        if (membership.Status != expectedMembershipStatus
            || payment.Status != expectedPaymentStatus
            || payment.ClientId != membership.ClientId
            || payment.MembershipId != membership.MembershipId
            || payment.Amount != membership.PriceAmount
            || payment.Currency != membership.PriceCurrency
            || payment.Method != "cash"
            || payment.PaymentContext != "membership_sale"
            || payment.EntryOrigin != membership.EntryOrigin
            || payment.EntryBatchId != membership.EntryBatchId
            || payment.Comment != membership.Comment
            || !AuditTimestampPrecision.IsSamePostgreSqlInstant(
                payment.RecordedAt,
                membership.IssuedAt))
        {
            throw new JsonException(
                "Issued Membership and exact sale Payment are inconsistent.");
        }
    }

    private void ValidateIssuedMembership(IssuedSaleMembershipSnapshot membership)
    {
        ValidateEntryBatch(membership.EntryOrigin, membership.EntryBatchId);

        DateOnly expectedBaseEndDate;
        try
        {
            expectedBaseEndDate = membership.StartDate.AddDays(
                membership.DurationDays - 1);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException(
                "Issued Membership dates are outside the supported range.",
                exception);
        }

        if (membership.IssuanceMode != "sale"
            || membership.StartDate > membership.BaseEndDate
            || membership.BaseEndDate != expectedBaseEndDate
            || membership.PriceCurrency
                != membership.PriceCurrency.Trim().ToUpperInvariant())
        {
            throw new JsonException("Issued Membership sale terms are inconsistent.");
        }
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel>
        IssuedSaleMembershipFacts(IssuedSaleMembershipSnapshot membership)
    {
        return
        [
            Fact("Membership", TimelineModel.ShortId(membership.MembershipId)),
            Fact("Client", TimelineModel.ShortId(membership.ClientId)),
            Fact("Type snapshot", membership.TypeName),
            Fact("Duration", Presentation.Days(membership.DurationDays)),
            Fact("Visit limit", Presentation.Number(membership.VisitsLimit)),
            Fact(
                "Snapshot price",
                MoneyLabel(membership.PriceAmount, membership.PriceCurrency)),
            Fact("Start date", DateLabel(membership.StartDate)),
            Fact("Base end date", DateLabel(membership.BaseEndDate)),
            Fact("Status", StatusLabel(membership.Status)),
        ];
    }

    private static CreatedPaymentSnapshot ReadCreatedPayment(JsonElement payment)
    {
        var amount = RequireDecimal(payment, "amount");
        if (amount <= 0)
        {
            throw new JsonException("Created Payment amount must be positive.");
        }

        return new CreatedPaymentSnapshot(
            RequireGuid(payment, "paymentId"),
            RequireGuid(payment, "clientId"),
            RequireNullableGuid(payment, "membershipId"),
            ReadOptionalGuid(payment, "negativeClosureId"),
            amount,
            RequireString(payment, "currency"),
            RequireString(payment, "method"),
            RequireString(payment, "paymentContext"),
            RequireTimestamp(payment, "occurredAt"),
            RequireTimestamp(payment, "recordedAt"),
            RequireString(payment, "entryOrigin"),
            RequireNullableGuid(payment, "entryBatchId"),
            RequireNullableString(payment, "comment"),
            RequireString(payment, "status"));
    }

    private static FreezeSnapshot ReadFreeze(JsonElement freeze)
    {
        var startDate = RequireDateOnly(freeze, "startDate");
        var endDate = RequireDateOnly(freeze, "endDate");
        var inclusiveDays = RequirePositiveInt32(freeze, "inclusiveDays");
        if (startDate > endDate
            || inclusiveDays != endDate.DayNumber - startDate.DayNumber + 1)
        {
            throw new JsonException("Freeze range summary is inconsistent.");
        }

        return new FreezeSnapshot(
            RequireGuid(freeze, "freezeId"),
            RequireGuid(freeze, "clientId"),
            RequireGuid(freeze, "membershipId"),
            startDate,
            endDate,
            inclusiveDays,
            RequireString(freeze, "reason"),
            RequireString(freeze, "status"));
    }

    private static FreezeMembershipStateSnapshot ReadFreezeMembershipState(
        JsonElement summary)
    {
        var state = RequireObject(summary, "membershipState");
        var snapshot = new FreezeMembershipStateSnapshot(
            RequireGuid(state, "membershipId"),
            RequireGuid(state, "clientId"),
            RequireInt32(state, "remainingVisits"),
            RequireNonNegativeInt32(state, "negativeBalance"),
            RequireNonNegativeInt32(state, "extensionDays"),
            RequireDateOnly(state, "effectiveEndDate"));
        _ = RequireStringArray(state, "warnings");
        return snapshot;
    }

    private static VisitMarkedSnapshot ReadMarkedVisit(JsonElement visit)
    {
        return new VisitMarkedSnapshot(
            RequireGuid(visit, "visitId"),
            RequireGuid(visit, "clientId"),
            RequireString(visit, "visitKind"),
            RequireNullableGuid(visit, "membershipId"),
            RequireTimestamp(visit, "occurredAt"),
            RequireTimestamp(visit, "recordedAt"),
            RequireString(visit, "entryOrigin"),
            RequireNullableGuid(visit, "entryBatchId"),
            RequireNullableString(visit, "comment"),
            RequireString(visit, "status"),
            RequireNullableGuid(visit, "consumptionId"),
            RequireStringArray(visit, "acknowledgements"),
            RequireString(visit, "selection"));
    }

    private static VisitMarkedMembershipStateSnapshot ReadVisitMarkedMembershipState(
        JsonElement state)
    {
        var firstNegativeVisitId = RequireNullableGuid(
            state,
            "firstNegativeVisitId");
        var firstNegativeVisitDate = RequireNullableDateOnly(
            state,
            "firstNegativeVisitDate");
        if ((firstNegativeVisitId is null) != (firstNegativeVisitDate is null))
        {
            throw new JsonException(
                "First-negative Visit metadata is incomplete.");
        }

        return new VisitMarkedMembershipStateSnapshot(
            RequireGuid(state, "membershipId"),
            RequireNonNegativeInt32(state, "countedVisits"),
            RequireInt32(state, "remainingVisits"),
            RequireNonNegativeInt32(state, "negativeBalance"),
            firstNegativeVisitId,
            firstNegativeVisitDate,
            RequireNonNegativeInt32(state, "extensionDays"),
            RequireDateOnly(state, "effectiveEndDate"),
            RequireNullableTimestamp(state, "lastCountedVisitAt"),
            RequireStringArray(state, "warnings"));
    }

    private void AddVisitMarkedMembershipFacts(
        ICollection<AuditEntryExplanationFactViewModel> facts,
        VisitMarkedMembershipStateSnapshot? state)
    {
        if (state is null)
        {
            return;
        }

        facts.Add(Fact(
            "Counted visits",
            Presentation.Number(state.CountedVisits)));
        facts.Add(Fact(
            "Remaining visits",
            Presentation.Number(state.RemainingVisits)));
        facts.Add(Fact(
            "Negative balance",
            Presentation.Number(state.NegativeBalance)));
        facts.Add(Fact(
            "First negative visit date",
            state.FirstNegativeVisitDate is { } date
                ? DateLabel(date)
                : Presentation.Value("NotRecorded")));
        facts.Add(Fact(
            "Membership warnings",
            MembershipWarningsLabel(state.Warnings)));
    }

    private static MembershipStateSnapshot? ReadMembershipState(
        JsonElement summary,
        Guid? membershipId)
    {
        var state = RequireNullableObject(summary, "membershipState");
        if (membershipId is null)
        {
            if (state is not null)
            {
                throw new JsonException(
                    "A non-membership Visit cannot include Membership state.");
            }

            return null;
        }

        if (state is null || RequireGuid(state.Value, "membershipId") != membershipId)
        {
            throw new JsonException("Membership Visit state is unavailable or inconsistent.");
        }

        return new MembershipStateSnapshot(
            RequireInt32(state.Value, "remainingVisits"),
            RequireInt32(state.Value, "negativeBalance"));
    }

    private void AddMembershipFacts(
        ICollection<AuditEntryExplanationFactViewModel> facts,
        MembershipStateSnapshot? membershipState)
    {
        if (membershipState is null)
        {
            return;
        }

        facts.Add(Fact(
            "Remaining visits",
            Presentation.Number(membershipState.RemainingVisits)));
        facts.Add(Fact(
            "Negative balance",
            Presentation.Number(membershipState.NegativeBalance)));
    }

    private string JoinChanged(params string[] keys) =>
        string.Join(", ", keys.Select(Presentation.Changed));

    private AuditEntryExplanationFactViewModel Fact(string key, string value)
    {
        var semanticKey = key switch
        {
            "Membership type" => "MembershipType",
            "Membership" => "Membership",
            "Client" => "Client",
            "Visit" => "Visit",
            "Visit type" => "VisitType",
            "Payment" => "Payment",
            "Freeze" => "Freeze",
            "Opening state" => "OpeningState",
            "Opening as of" => "OpeningAsOf",
            "Source reference" => "SourceReference",
            "Amount" => "Amount",
            "Method" => "Method",
            "Context" => "Context",
            "Consumption" => "Consumption",
            "Selection" => "Selection",
            "Warning acknowledgements" => "WarningAcknowledgements",
            "Status" => "Status",
            "Source status" => "SourceStatus",
            "Original status" => "OriginalStatus",
            "Reason comment" => "ReasonComment",
            "Original fact" => "OriginalFact",
            "Occurred" => "Occurred",
            "Created" => "Created",
            "Deactivated" => "Deactivated",
            "Entry origin" => "EntryOrigin",
            "Original entry origin" => "OriginalEntryOrigin",
            "Period" => "Period",
            "Freeze reason" => "FreezeReason",
            "Cancellation recorded" => "CancellationRecorded",
            "Effective end" => "EffectiveEnd",
            "Negative handling" => "NegativeHandling",
            "Type snapshot" => "TypeSnapshot",
            "Base end date" => "BaseEndDate",
            "Start date" => "StartDate",
            "Name" => "Name",
            "Catalog comment" => "CatalogComment",
            "Duration" => "Duration",
            "Visit limit" => "VisitLimit",
            "Price" => "Price",
            "Declared remaining visits" => "DeclaredRemainingVisits",
            "Declared negative balance" => "DeclaredNegativeBalance",
            "Known effective end" => "KnownEffectiveEnd",
            "Known extension" => "KnownExtension",
            "Entry batch" => "EntryBatch",
            "Recalculated remaining visits" => "RecalculatedRemainingVisits",
            "Recalculated negative balance" => "RecalculatedNegativeBalance",
            "Recalculated effective end" => "RecalculatedEffectiveEnd",
            "Recalculated extension" => "RecalculatedExtension",
            "Recalculation version" => "RecalculationVersion",
            "Inclusive days" => "InclusiveDays",
            "Extension days" => "ExtensionDays",
            "Counted visits" => "CountedVisits",
            "Remaining visits" => "RemainingVisits",
            "Negative balance" => "NegativeBalance",
            "First negative visit date" => "FirstNegativeVisitDate",
            "Membership warnings" => "MembershipWarnings",
            "Existing negative balance" => "ExistingNegativeBalance",
            "Snapshot price" => "SnapshotPrice",
            "Initial counted visits" => "InitialCountedVisits",
            "Initial remaining visits" => "InitialRemainingVisits",
            "Initial negative balance" => "InitialNegativeBalance",
            "Initial extension days" => "InitialExtensionDays",
            "Initial effective end date" => "InitialEffectiveEndDate",
            "Initial first negative visit date" => "InitialFirstNegativeVisitDate",
            "Payment record" => "PaymentRecord",
            "Negative closure" => "NegativeClosure",
            "Covered visits" => "CoveredVisits",
            "Remaining negative balance" => "RemainingNegativeBalance",
            "Forced coverage start" => "ForcedCoverageStart",
            "Correction" => "Correction",
            "Replacement" => "Replacement",
            "Closure type" => "ClosureType",
            "Paper fallback batch" => "PaperFallbackBatch",
            "Paper sheet" => "PaperSheet",
            "Business dates" => "BusinessDates",
            "Batch note" => "BatchNote",
            "Paper row" => "PaperRow",
            "Line number" => "LineNumber",
            "Event type" => "EventType",
            "Explanation" => "Explanation",
            "Recorded" => "Recorded",
            "Recorded by" => "RecordedBy",
            "Session" => "Session",
            _ => throw new InvalidOperationException(
                $"Unsupported Audit explanation fact label '{key}'."),
        };
        return new AuditEntryExplanationFactViewModel(
            Presentation.Fact(semanticKey),
            value);
    }

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return value;
    }

    private static JsonElement? RequireNullableObject(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.Object => value,
            _ => throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid shape."),
        };
    }

    private static string RequireString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return value.GetString()!;
    }

    private static string? RequireNullableString(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid value.");
        }

        return value.GetString();
    }

    private static Guid RequireGuid(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !value.TryGetGuid(out var result)
            || result == Guid.Empty)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static Guid? RequireNullableGuid(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !value.TryGetGuid(out var result)
            || result == Guid.Empty)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid value.");
        }

        return result;
    }

    private static DateTimeOffset RequireTimestamp(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var result)
            || result == default)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static DateOnly RequireDateOnly(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(
                value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result)
            || result == default)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static DateOnly? RequireNullableDateOnly(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !DateOnly.TryParseExact(
                value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result)
            || result == default)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid value.");
        }

        return result;
    }

    private static DateTimeOffset? RequireNullableTimestamp(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !value.TryGetDateTimeOffset(out var result)
            || result == default)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid value.");
        }

        return result;
    }

    private static bool RequireBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || (value.ValueKind != JsonValueKind.True
                && value.ValueKind != JsonValueKind.False))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return value.GetBoolean();
    }

    private static decimal RequireDecimal(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var result)
            || result <= 0)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static decimal RequireNonNegativeDecimal(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDecimal(out var result)
            || result < 0)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static int RequirePositiveInt32(JsonElement parent, string propertyName)
    {
        var result = RequireInt32(parent, propertyName);
        if (result <= 0)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static int RequireNonNegativeInt32(JsonElement parent, string propertyName)
    {
        var result = RequireInt32(parent, propertyName);
        if (result < 0)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        return result;
    }

    private static int? RequireNullableNonNegativeInt32(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < 0)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' has an invalid value.");
        }

        return result;
    }

    private static IReadOnlyList<string> RequireStringArray(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        var items = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new JsonException(
                    $"Audit summary property '{propertyName}' has an invalid item.");
            }

            items.Add(item.GetString()!);
        }

        return items;
    }

    private static IReadOnlyList<Guid> RequireGuidArray(
        JsonElement parent,
        string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"Audit summary property '{propertyName}' is required.");
        }

        var items = new List<Guid>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !Guid.TryParse(item.GetString(), out var id)
                || id == Guid.Empty)
            {
                throw new JsonException(
                    $"Audit summary property '{propertyName}' has an invalid item.");
            }

            items.Add(id);
        }

        return items;
    }

    private string MoneyLabel(decimal amount, string currency)
    {
        return Presentation.Money(new BodyLife.Crm.SharedKernel.Money(amount, currency));
    }

    private string FreezeRangeLabel(FreezeSnapshot freeze)
    {
        return Presentation.Text("Template.DateRange", DateLabel(freeze.StartDate), DateLabel(freeze.EndDate));
    }

    private string DateLabel(DateOnly date)
    {
        return Presentation.Date(date);
    }

    private void ValidateEntryBatch(string entryOrigin, Guid? entryBatchId)
    {
        _ = StoredEntryOriginLabel(entryOrigin);
        if (entryOrigin == "normal" && entryBatchId is not null)
        {
            throw new JsonException("A normal audit summary cannot reference an entry batch.");
        }
    }

    private static string EntryOriginValue(EntryOrigin entryOrigin)
    {
        return entryOrigin switch
        {
            EntryOrigin.Normal => "normal",
            EntryOrigin.ManualBackfill => "manual_backfill",
            EntryOrigin.PaperFallback => "paper_fallback",
            EntryOrigin.FutureImport => "future_import",
            _ => throw new JsonException("Entry origin is not supported."),
        };
    }

    private string StoredEntryOriginLabel(string entryOrigin)
    {
        return entryOrigin switch
        {
            "normal" => Presentation.EntryOrigin(EntryOrigin.Normal),
            "manual_backfill" => Presentation.EntryOrigin(EntryOrigin.ManualBackfill),
            "paper_fallback" => Presentation.EntryOrigin(EntryOrigin.PaperFallback),
            "future_import" => Presentation.EntryOrigin(EntryOrigin.FutureImport),
            _ => throw new JsonException("Stored entry origin is not supported."),
        };
    }

    private string OptionalIdLabel(Guid? id)
    {
        return id is { } value ? Presentation.ShortId(value) : Presentation.Value("NoMembership");
    }

    private string VisitKindLabel(string value)
    {
        return value switch
        {
            "membership" => Presentation.VisitKind(BodyLife.Crm.Modules.Visits.VisitKind.Membership),
            "one_off" => Presentation.VisitKind(BodyLife.Crm.Modules.Visits.VisitKind.OneOff),
            "trial" => Presentation.VisitKind(BodyLife.Crm.Modules.Visits.VisitKind.Trial),
            _ => throw new JsonException("Visit kind is not supported."),
        };
    }

    private string VisitSelectionLabel(string visitKind)
    {
        return visitKind switch
        {
            "membership" => Presentation.Text("Visit.Selection.Membership"),
            "one_off" => Presentation.Text("Visit.Selection.OneOff"),
            "trial" => Presentation.Text("Visit.Selection.Trial"),
            _ => throw new JsonException("Visit kind is not supported."),
        };
    }

    private string VisitAcknowledgementsLabel(
        IReadOnlyList<string> acknowledgements)
    {
        return LabelsOrNone(
            acknowledgements,
            acknowledgement => acknowledgement switch
            {
                "expired" => Presentation.Text("Visit.Warning.Expired"),
                "zero_remaining" => Presentation.Text("Visit.Warning.ZeroRemaining"),
                "negative_remaining" => Presentation.Text("Visit.Warning.NegativeRemaining"),
                _ => throw new JsonException(
                    "Visit warning acknowledgement is not supported."),
            });
    }

    private string MembershipWarningsLabel(IReadOnlyList<string> warnings)
    {
        return LabelsOrNone(
            warnings,
            warning => warning switch
            {
                "membership_negative_balance" => Presentation.Text("Membership.Warning.NegativeBalance"),
                "membership_expired_by_date" => Presentation.Text("Membership.Warning.Expired"),
                "membership_zero_remaining" => Presentation.Text("Membership.Warning.ZeroRemaining"),
                "membership_ending_soon" => Presentation.Text("Membership.Warning.EndingSoon"),
                "membership_low_remaining" => Presentation.Text("Membership.Warning.LowRemaining"),
                _ => throw new JsonException("Membership warning is not supported."),
            });
    }

    private string LabelsOrNone(
        IReadOnlyList<string> values,
        Func<string, string> label)
    {
        if (values.Count == 0)
        {
            return Presentation.Value("None");
        }

        var distinct = values.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length != values.Count)
        {
            throw new JsonException("Audit summary values must be unique.");
        }

        return string.Join(", ", values.Select(label));
    }

    private string ConsumptionStatusLabel(string? value)
    {
        return value switch
        {
            "active" => Presentation.Status("Active"),
            "canceled" => Presentation.Status("Canceled"),
            null => Presentation.Value("NotApplicable"),
            _ => throw new JsonException("Visit consumption status is not supported."),
        };
    }

    private string PaymentContextLabel(string value)
    {
        return value switch
        {
            "membership_sale" => Presentation.PaymentContext(BodyLife.Crm.Modules.Payments.PaymentContext.MembershipSale),
            "one_off" => Presentation.PaymentContext(BodyLife.Crm.Modules.Payments.PaymentContext.OneOff),
            "trial" => Presentation.PaymentContext(BodyLife.Crm.Modules.Payments.PaymentContext.Trial),
            "negative_closure" => Presentation.PaymentContext(BodyLife.Crm.Modules.Payments.PaymentContext.NegativeClosure),
            "other" => Presentation.PaymentContext(BodyLife.Crm.Modules.Payments.PaymentContext.Other),
            _ => throw new JsonException("Payment context is not supported."),
        };
    }

    private string PaymentMethodLabel(string value)
    {
        return value switch
        {
            "cash" => Presentation.Text("Payment.Method.Cash"),
            _ => throw new JsonException("Payment method is not supported."),
        };
    }

    private string MembershipStatusLabel(string value)
    {
        return value switch
        {
            "active" => Presentation.Status("Active"),
            _ => throw new JsonException("Membership status is not supported."),
        };
    }

    private string MembershipNegativeHandlingLabel(string? value)
    {
        return value switch
        {
            null => Presentation.Text("NegativeHandling.NotRequired"),
            "leave_visible" => Presentation.Text("NegativeHandling.LeaveVisible"),
            "cover_with_new_membership" => Presentation.Text("NegativeHandling.CoverWithNewMembership"),
            "record_explicit_closure" => Presentation.Text("NegativeHandling.RecordExplicitClosure"),
            _ => throw new JsonException(
                "Membership negative handling decision is not supported."),
        };
    }

    private string MembershipNegativeCoveragePolicyLabel(string value)
    {
        return value switch
        {
            "automatic_oldest_first" => Presentation.Text("NegativeHandling.AutomaticOldestFirst"),
            _ => throw new JsonException("Membership negative coverage policy is not supported."),
        };
    }

    private string StatusLabel(string value)
    {
        return value switch
        {
            "active" => Presentation.Status("Active"),
            "corrected" => Presentation.Status("Corrected"),
            "replaced" => Presentation.Status("Replaced"),
            "canceled" => Presentation.Status("Canceled"),
            _ => throw new JsonException("Payment status is not supported."),
        };
    }

    private string ChangedFieldLabel(string value)
    {
        return value switch
        {
            "amount" => Presentation.ChangedField("amount"),
            "currency" => Presentation.Text("Changed.Currency"),
            "occurred_at" => Presentation.ChangedField("occurred_at"),
            "payment_context" => Presentation.ChangedField("payment_context"),
            "membership_id" => Presentation.ChangedField("membership_id"),
            "comment" => Presentation.ChangedField("comment"),
            _ => throw new JsonException("Payment correction field is not supported."),
        };
    }

    private sealed record PaymentSnapshot(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record IssuedSaleMembershipSnapshot(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId,
        string TypeName,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        string IssuanceMode,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);

    private sealed record IssuedSalePaymentSnapshot(
        Guid PaymentId,
        Guid ClientId,
        Guid MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);

    private sealed record CreatedPaymentSnapshot(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        Guid? NegativeClosureId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record MembershipIssueSnapshot(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId,
        MembershipIssueTermsSnapshot Snapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string? NegativeHandlingDecision,
        string? NegativeCoveragePolicy,
        MembershipIssueExistingNegativeStateSnapshot? ExistingNegativeState,
        MembershipIssueNegativeCoverageSnapshot? NegativeCoverage,
        MembershipIssuePaymentSnapshot? Payment,
        MembershipIssueInitialStateSnapshot InitialState);

    private sealed record MembershipIssueTermsSnapshot(
        string TypeName,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency);

    private sealed record MembershipIssueExistingNegativeStateSnapshot(
        int NegativeBalance,
        DateOnly? FirstNegativeVisitDate,
        int OpenConcreteVisitCount,
        int UnknownNegativeBalance);

    private sealed record MembershipIssueNegativeCoverageSnapshot(
        Guid NegativeClosureId,
        int Count,
        IReadOnlyList<Guid> CoveredVisitIds,
        int RemainingExistingNegativeBalance,
        DateOnly ForcedStartDate,
        bool IsAlreadyExpiredAtIssue);

    private sealed record MembershipIssuePaymentSnapshot(
        Guid PaymentId,
        decimal Amount,
        string Currency,
        string Method);

    private sealed record MembershipIssueInitialStateSnapshot(
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate);

    private sealed record FreezeSnapshot(
        Guid FreezeId,
        Guid ClientId,
        Guid MembershipId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string Reason,
        string Status);

    private sealed record FreezeMembershipStateSnapshot(
        Guid MembershipId,
        Guid ClientId,
        int RemainingVisits,
        int NegativeBalance,
        int ExtensionDays,
        DateOnly EffectiveEndDate);

    private sealed record MembershipTypeCatalogSnapshot(
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt)
    {
        internal bool HasValidLifecycle()
        {
            return CreatedAt <= UpdatedAt
                && (IsActive
                    ? DeactivatedAt is null
                    : DeactivatedAt is not null && DeactivatedAt >= CreatedAt);
        }

        internal MembershipTypeCatalogValues CatalogValues()
        {
            return new MembershipTypeCatalogValues(
                Name,
                DurationDays,
                VisitsLimit,
                PriceAmount,
                PriceCurrency,
                Comment);
        }
    }

    private sealed record MembershipTypeCatalogValues(
        string Name,
        int DurationDays,
        int VisitsLimit,
        decimal PriceAmount,
        string PriceCurrency,
        string? Comment);

    private sealed record MembershipOpeningStateCreationSnapshot(
        Guid OpeningStateId,
        Guid MembershipId,
        Guid ClientId,
        DateOnly OpeningAsOfDate,
        int DeclaredRemainingVisits,
        int DeclaredNegativeBalance,
        DateOnly? KnownEffectiveEndDate,
        int? KnownExtensionDays,
        string SourceReference,
        Guid? EntryBatchId,
        string Status,
        MembershipOpeningStateRecalculatedSnapshot RecalculatedState);

    private sealed record MembershipOpeningStateRecalculatedSnapshot(
        int RemainingVisits,
        int NegativeBalance,
        DateOnly EffectiveEndDate,
        int ExtensionDays,
        int RecalculationVersion);

    private sealed record MembershipStateSnapshot(
        int RemainingVisits,
        int NegativeBalance);

    private sealed record VisitMarkedSnapshot(
        Guid VisitId,
        Guid ClientId,
        string VisitKind,
        Guid? MembershipId,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status,
        Guid? ConsumptionId,
        IReadOnlyList<string> Acknowledgements,
        string Selection);

    private sealed record PaperReferenceSnapshot(
        Guid EntryBatchId,
        Guid EntryBatchRowId,
        string PaperSheetNumber,
        int LineNumber,
        string Explanation);

    private sealed record NegativeClosureAuditLineSnapshot(
        Guid LineId,
        int Sequence,
        Guid MembershipTypeId,
        string TypeName,
        int Quantity,
        decimal UnitPrice,
        string Currency,
        decimal LineTotal);

    private sealed record NegativeClosureCorrectionLineSnapshot(
        Guid LineId,
        Guid MembershipTypeId,
        string TypeName,
        int Quantity,
        decimal UnitPrice,
        string Currency,
        decimal LineTotal);

    private sealed record NegativeClosureLifecyclePaymentSnapshot(
        Guid PaymentId,
        Guid ClientId,
        Guid NegativeClosureId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);

    private sealed record VisitMarkedMembershipStateSnapshot(
        Guid MembershipId,
        int CountedVisits,
        int RemainingVisits,
        int NegativeBalance,
        Guid? FirstNegativeVisitId,
        DateOnly? FirstNegativeVisitDate,
        int ExtensionDays,
        DateOnly EffectiveEndDate,
        DateTimeOffset? LastCountedVisitAt,
        IReadOnlyList<string> Warnings);
}

public sealed record AuditEntryExplanationFactViewModel(
    string Label,
    string Value);
