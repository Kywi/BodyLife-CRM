using System.Globalization;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;

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
            ["membership_opening_state.created"] = "membership-opening-state-created",
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
                "membership_opening_state.created"
                    when entry.EntityType == AuditTimelineEntityType.MembershipOpeningState
                    => CreateMembershipOpeningState(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
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
                        before.RootElement,
                        after.RootElement),
                "visit.marked" when entry.EntityType == AuditTimelineEntityType.Visit
                    => CreateVisitMarked(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "visit.canceled" when entry.EntityType == AuditTimelineEntityType.Visit
                    => CreateVisitCancellation(entry, before.RootElement, after.RootElement),
                "payment.created" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCreation(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "payment.corrected" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCorrection(entry, before.RootElement, after.RootElement),
                "payment.canceled" when entry.EntityType == AuditTimelineEntityType.Payment
                    => CreatePaymentCancellation(entry, before.RootElement, after.RootElement),
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
            || created.CreatedAt != entry.RecordedAt
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

        ValidateEntryBatch(visit.EntryOrigin, visit.EntryBatchId);
        if (visit.VisitId != entry.EntityId
            || visit.ClientId != relatedClientId
            || visit.MembershipId != relatedMembershipId
            || visit.ConsumptionId != relatedConsumptionId
            || visit.OccurredAt != entry.OccurredAt
            || visit.RecordedAt != entry.RecordedAt
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

    private AuditEntryExplanationViewModel CreateVisitCancellation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var originalVisit = RequireObject(before, "visit");
        var canceledVisit = RequireObject(after, "visit");
        var cancellation = RequireObject(after, "cancellation");

        var visitId = RequireGuid(originalVisit, "visitId");
        if (visitId != entry.EntityId
            || RequireGuid(canceledVisit, "visitId") != visitId
            || RequireGuid(cancellation, "visitId") != visitId
            || RequireGuid(cancellation, "cancellationId") == Guid.Empty
            || RequireString(originalVisit, "status") != "active"
            || RequireString(canceledVisit, "status") != "canceled")
        {
            throw new JsonException("Visit cancellation summary identity is inconsistent.");
        }

        var membershipId = RequireNullableGuid(originalVisit, "membershipId");
        var originalConsumptionStatus = RequireNullableString(
            originalVisit,
            "consumptionStatus");
        var canceledConsumptionStatus = RequireNullableString(
            canceledVisit,
            "consumptionStatus");
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
            || RequireTimestamp(cancellation, "occurredAt") != entry.OccurredAt
            || cancellationRecordedAt != entry.RecordedAt
            || cancellationEntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || RequireBoolean(cancellation, "changedAfterClose")
                != entry.ChangedAfterClose)
        {
            throw new JsonException("Freeze cancellation summary is inconsistent.");
        }

        var membershipStateChanged =
            beforeMembership.ExtensionDays != afterMembership.ExtensionDays
            || beforeMembership.EffectiveEndDate != afterMembership.EffectiveEndDate;

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
            [
                Fact("Original fact", Presentation.Value("Preserved")),
                Fact("Status", Presentation.Status("Canceled")),
                Fact(
                    "Extension days",
                    Presentation.Days(afterMembership.ExtensionDays)),
                Fact("Effective end", DateLabel(afterMembership.EffectiveEndDate)),
                Fact("Cancellation recorded", TimelineModel.TimestampLabel(cancellationRecordedAt)),
            ],
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
            || occurredAt != entry.OccurredAt
            || recordedAt != entry.RecordedAt
            || entryOrigin != EntryOriginValue(entry.EntryOrigin)
            || freeze.Status != "active")
        {
            throw new JsonException("Freeze addition summary is inconsistent.");
        }

        var membershipStateChanged =
            beforeMembership.ExtensionDays != afterMembership.ExtensionDays
            || beforeMembership.EffectiveEndDate != afterMembership.EffectiveEndDate;

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
                Fact(
                    "Extension days",
                    Presentation.Days(afterMembership.ExtensionDays)),
                Fact("Effective end", DateLabel(afterMembership.EffectiveEndDate)),
            ],
            ChangedFields: membershipStateChanged
                ? JoinChanged("FreezeSource", "MembershipExtensionState")
                : Presentation.Changed("FreezeSource"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaymentCorrection(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var original = ReadPayment(RequireObject(before, "payment"));
        var preservedOriginal = ReadPayment(RequireObject(after, "originalPayment"));
        var replacement = ReadPayment(RequireObject(after, "replacementPayment"));
        var correction = RequireObject(after, "correction");
        var changedFields = RequireStringArray(correction, "changedFields");

        if (original.PaymentId != entry.EntityId
            || preservedOriginal.PaymentId != original.PaymentId
            || RequireGuid(correction, "originalPaymentId") != original.PaymentId
            || RequireGuid(correction, "replacementPaymentId") != replacement.PaymentId
            || RequireGuid(correction, "correctionId") == Guid.Empty
            || replacement.PaymentId == original.PaymentId
            || original.Status != "active"
            || preservedOriginal.Status != "replaced"
            || (preservedOriginal with { Status = "active" }) != original
            || replacement.Status != "active"
            || changedFields.Count == 0)
        {
            throw new JsonException("Payment correction summary identity is inconsistent.");
        }

        return CreateExplanation("PaymentCorrected",
            "payment-corrected",
            PaymentFacts(original),
            [
                Fact("Original status", Presentation.Status("Replaced")),
                .. PaymentFacts(replacement),
            ],
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
        var payment = ReadCreatedPayment(RequireObject(after, "payment"));
        ValidateEntryBatch(payment.EntryOrigin, payment.EntryBatchId);

        if (payment.PaymentId != entry.EntityId
            || payment.ClientId != relatedClientId
            || payment.MembershipId != relatedMembershipId
            || payment.OccurredAt != entry.OccurredAt
            || payment.RecordedAt != entry.RecordedAt
            || payment.EntryOrigin != EntryOriginValue(entry.EntryOrigin)
            || payment.Comment != entry.Comment
            || payment.Method != "cash"
            || payment.Status != "active")
        {
            throw new JsonException("Created Payment summary is inconsistent.");
        }

        var context = PaymentContextLabel(payment.PaymentContext);
        return CreateExplanation("PaymentCreated",
            "payment-created",
            [
                Fact("Payment", Presentation.Value("NotPresent")),
            ],
            [
                Fact("Payment", TimelineModel.ShortId(payment.PaymentId)),
                Fact("Client", TimelineModel.ShortId(payment.ClientId)),
                Fact("Amount", MoneyLabel(payment.Amount, payment.Currency)),
                Fact("Method", PaymentMethodLabel(payment.Method)),
                Fact("Context", context),
                Fact("Membership", OptionalIdLabel(payment.MembershipId)),
                Fact("Occurred", TimelineModel.TimestampLabel(payment.OccurredAt)),
                Fact("Status", Presentation.Status("Active")),
            ],
            ChangedFields: Presentation.Changed("Payment"),
            IsAvailable: true);
    }

    private AuditEntryExplanationViewModel CreatePaymentCancellation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var original = ReadPayment(RequireObject(before, "payment"));
        var canceled = ReadPayment(RequireObject(after, "payment"));
        var cancellation = RequireObject(after, "cancellation");

        if (original.PaymentId != entry.EntityId
            || canceled.PaymentId != original.PaymentId
            || RequireGuid(cancellation, "paymentId") != original.PaymentId
            || RequireGuid(cancellation, "cancellationId") == Guid.Empty
            || original.Status != "active"
            || canceled.Status != "canceled"
            || (canceled with { Status = "active" }) != original)
        {
            throw new JsonException("Payment cancellation summary identity is inconsistent.");
        }

        return CreateExplanation("PaymentCanceled",
            "payment-canceled",
            PaymentFacts(original),
            PaymentFacts(canceled),
            ChangedFields: Presentation.Changed("PaymentStatus"),
            IsAvailable: true);
    }

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

        if (issue.MembershipId != entry.EntityId
            || issue.ClientId != relatedClientId
            || issue.MembershipTypeId != relatedMembershipTypeId
            || issue.Payment?.PaymentId != relatedPaymentId
            || issue.StartDate > issue.BaseEndDate
            || issue.IssuedAt != entry.RecordedAt
            || issue.Status != "active"
            || (issue.NegativeHandlingDecision is null)
                != (issue.ExistingNegativeState is null))
        {
            throw new JsonException("Membership issue summary identity is inconsistent.");
        }

        var negativeHandling = MembershipNegativeHandlingLabel(
            issue.NegativeHandlingDecision);
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
        if (issue.ExistingNegativeState is { } existingNegativeState)
        {
            beforeFacts.Add(Fact(
                "First negative visit date",
                DateLabel(existingNegativeState.FirstNegativeVisitDate)));
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
            Fact("Negative handling", negativeHandling),
        ];
        if (issue.InitialState.FirstNegativeVisitDate is { } firstNegativeVisitDate)
        {
            afterFacts.Add(Fact(
                "Initial first negative visit date",
                DateLabel(firstNegativeVisitDate)));
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

        return CreateExplanation("MembershipIssued",
            "membership-issued",
            beforeFacts,
            afterFacts,
            ChangedFields: Presentation.Changed("IssuedMembership"),
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
            RequireNullableString(summary, "negativeHandlingDecision"),
            ReadExistingNegativeState(summary),
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
                RequireDateOnly(existingState.Value, "firstNegativeVisitDate"));
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
            RequireNullableGuid(payment, "membershipId"),
            RequireDecimal(payment, "amount"),
            RequireString(payment, "currency"),
            RequireString(payment, "method"),
            RequireString(payment, "paymentContext"),
            RequireTimestamp(payment, "occurredAt"),
            RequireString(payment, "status"));
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

    private string StatusLabel(string value)
    {
        return value switch
        {
            "active" => Presentation.Status("Active"),
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
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        string Status);

    private sealed record CreatedPaymentSnapshot(
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

    private sealed record MembershipIssueSnapshot(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId,
        MembershipIssueTermsSnapshot Snapshot,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        string Status,
        string? NegativeHandlingDecision,
        MembershipIssueExistingNegativeStateSnapshot? ExistingNegativeState,
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
        DateOnly FirstNegativeVisitDate);

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
