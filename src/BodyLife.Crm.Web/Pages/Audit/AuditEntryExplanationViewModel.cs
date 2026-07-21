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
    bool IsAvailable)
{
    public static AuditEntryExplanationViewModel? Create(AuditTimelineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var kind = entry.ActionType switch
        {
            "membership_type.edited" => "membership-type-edited",
            "membership_type.deactivated" => "membership-type-deactivated",
            "membership.issued" => "membership-issued",
            "client.updated" => "client-updated",
            "card.assigned" => "card-assigned",
            "card.changed" => "card-changed",
            "card.cleared" => "card-cleared",
            "staff_account.created" => "staff-account-created",
            "staff_account.display_name_updated" => "staff-account-display-name-updated",
            "staff_account.activated" => "staff-account-activated",
            "staff_account.deactivated" => "staff-account-deactivated",
            "staff_credentials.configured" => "staff-credentials-configured",
            "staff_credentials.reset" => "staff-credentials-reset",
            "non_working_day.corrected" => "non-working-day-corrected",
            "non_working_day.canceled" => "non-working-day-canceled",
            "freeze.canceled" => "freeze-canceled",
            "visit.canceled" => "visit-canceled",
            "payment.corrected" => "payment-corrected",
            "payment.canceled" => "payment-canceled",
            _ => null,
        };
        if (kind is null)
        {
            return null;
        }

        try
        {
            using var related = JsonDocument.Parse(entry.RelatedEntityRefsJson);
            using var before = JsonDocument.Parse(entry.BeforeSummaryJson);
            using var after = JsonDocument.Parse(entry.AfterSummaryJson);
            return entry.ActionType switch
            {
                "client.updated" when entry.EntityType == AuditTimelineEntityType.Client
                    => ClientAuditExplanationFactory.CreateClientUpdate(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.assigned" when entry.EntityType == AuditTimelineEntityType.Client
                    => ClientAuditExplanationFactory.CreateCardAssignment(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.changed" when entry.EntityType == AuditTimelineEntityType.Client
                    => ClientAuditExplanationFactory.CreateCardChange(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "card.cleared" when entry.EntityType == AuditTimelineEntityType.Client
                    => ClientAuditExplanationFactory.CreateCardClear(
                        entry,
                        related.RootElement,
                        before.RootElement,
                        after.RootElement),
                "staff_account.created"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateAccountCreation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.display_name_updated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateDisplayNameUpdate(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.activated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateActivation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_account.deactivated"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateDeactivation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_credentials.configured"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateCredentialConfiguration(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "staff_credentials.reset"
                    when entry.EntityType == AuditTimelineEntityType.StaffAccount
                    => StaffAccountAuditExplanationFactory.CreateCredentialReset(
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
                "non_working_day.corrected"
                    when entry.EntityType == AuditTimelineEntityType.NonWorkingPeriod
                    => NonWorkingDayAuditExplanationFactory.CreateCorrection(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "non_working_day.canceled"
                    when entry.EntityType == AuditTimelineEntityType.NonWorkingPeriod
                    => NonWorkingDayAuditExplanationFactory.CreateCancellation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "freeze.canceled" when entry.EntityType == AuditTimelineEntityType.Freeze
                    => CreateFreezeCancellation(
                        entry,
                        before.RootElement,
                        after.RootElement),
                "visit.canceled" when entry.EntityType == AuditTimelineEntityType.Visit
                    => CreateVisitCancellation(entry, before.RootElement, after.RootElement),
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

    private static AuditEntryExplanationViewModel CreateMembershipTypeEdit(
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

        return new AuditEntryExplanationViewModel(
            "membership-type-edited",
            "Membership type catalog updated",
            "The catalog values changed for future Membership issues. Already issued Membership snapshots remain unchanged.",
            "Original catalog",
            "Updated catalog",
            MembershipTypeFacts(original),
            MembershipTypeFacts(updated),
            string.Join(", ", changedFields),
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreateMembershipTypeDeactivation(
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

        return new AuditEntryExplanationViewModel(
            "membership-type-deactivated",
            "Membership type deactivated",
            "The catalog record and already issued Membership history remain preserved. This type is no longer available for future ordinary issue.",
            "Before deactivation",
            "After deactivation",
            MembershipTypeFacts(original),
            [
                .. MembershipTypeFacts(deactivated),
                Fact(
                    "Deactivated",
                    TimelineModel.TimestampLabel(deactivated.DeactivatedAt.Value)),
            ],
            ChangedFields: "Catalog status",
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreateVisitCancellation(
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
            Fact("Status", "Active"),
            Fact("Occurred", TimelineModel.TimestampLabel(
                RequireTimestamp(originalVisit, "occurredAt"))),
            Fact("Membership", OptionalIdLabel(membershipId)),
            Fact("Consumption", ConsumptionStatusLabel(originalConsumptionStatus)),
        };
        AddMembershipFacts(beforeFacts, beforeMembership);

        var afterFacts = new List<AuditEntryExplanationFactViewModel>
        {
            Fact("Original fact", "Preserved"),
            Fact("Status", "Canceled"),
            Fact("Membership", OptionalIdLabel(membershipId)),
            Fact("Consumption", ConsumptionStatusLabel(canceledConsumptionStatus)),
        };
        AddMembershipFacts(afterFacts, afterMembership);

        var narrative = membershipId is null
            ? "The original Visit remains in history, and a separate cancellation record marks it canceled. No Membership consumption was involved."
            : "The original Visit remains in history. Its counted consumption was canceled, and the stored Membership state was reread after recalculation.";

        return new AuditEntryExplanationViewModel(
            "visit-canceled",
            "Original Visit preserved; cancellation added",
            narrative,
            "Original visit",
            "After cancellation",
            beforeFacts,
            afterFacts,
            ChangedFields: membershipId is null
                ? "Visit status"
                : "Visit status, consumption status, Membership state",
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreateFreezeCancellation(
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

        return new AuditEntryExplanationViewModel(
            "freeze-canceled",
            "Original Freeze preserved; cancellation added",
            "The original Freeze remains in history with Canceled status. The stored before/after Membership state comes from canonical recalculation; overlapping active extensions can leave the effective end unchanged.",
            "Original freeze",
            "After cancellation",
            [
                Fact("Period", FreezeRangeLabel(original)),
                Fact(
                    "Inclusive days",
                    original.InclusiveDays.ToString(CultureInfo.InvariantCulture)),
                Fact("Freeze reason", original.Reason),
                Fact("Status", "Active"),
                Fact("Original entry origin", StoredEntryOriginLabel(originalEntryOrigin)),
                Fact(
                    "Extension days",
                    beforeMembership.ExtensionDays.ToString(CultureInfo.InvariantCulture)),
                Fact("Effective end", DateLabel(beforeMembership.EffectiveEndDate)),
            ],
            [
                Fact("Original fact", "Preserved"),
                Fact("Status", "Canceled"),
                Fact(
                    "Extension days",
                    afterMembership.ExtensionDays.ToString(CultureInfo.InvariantCulture)),
                Fact("Effective end", DateLabel(afterMembership.EffectiveEndDate)),
                Fact("Cancellation recorded", TimelineModel.TimestampLabel(cancellationRecordedAt)),
            ],
            ChangedFields: membershipStateChanged
                ? "Freeze status, Membership extension state"
                : "Freeze status",
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreatePaymentCorrection(
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

        return new AuditEntryExplanationViewModel(
            "payment-corrected",
            "Original Payment preserved; replacement added",
            "The original Payment remains in history with Replaced status. The replacement is the active cash fact used by canonical reports.",
            "Original payment",
            "Replacement payment",
            PaymentFacts(original),
            [
                Fact("Original status", "Replaced"),
                .. PaymentFacts(replacement),
            ],
            string.Join(", ", changedFields.Select(ChangedFieldLabel)),
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreatePaymentCancellation(
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

        return new AuditEntryExplanationViewModel(
            "payment-canceled",
            "Original Payment preserved; cancellation added",
            "The original Payment remains in history, and a separate cancellation record marks it canceled for canonical cash totals.",
            "Original payment",
            "After cancellation",
            PaymentFacts(original),
            PaymentFacts(canceled),
            ChangedFields: "Payment status",
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel CreateMembershipIssue(
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
            Fact("Membership", "Not present"),
            Fact(
                "Existing negative balance",
                issue.ExistingNegativeState is null
                    ? "None"
                    : issue.ExistingNegativeState.NegativeBalance.ToString(
                        CultureInfo.InvariantCulture)),
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
                $"{issue.Snapshot.DurationDays.ToString(CultureInfo.InvariantCulture)} days"),
            Fact(
                "Visit limit",
                issue.Snapshot.VisitsLimit.ToString(CultureInfo.InvariantCulture)),
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
                issue.InitialState.CountedVisits.ToString(CultureInfo.InvariantCulture)),
            Fact(
                "Initial remaining visits",
                issue.InitialState.RemainingVisits.ToString(CultureInfo.InvariantCulture)),
            Fact(
                "Initial negative balance",
                issue.InitialState.NegativeBalance.ToString(CultureInfo.InvariantCulture)),
            Fact(
                "Initial extension days",
                issue.InitialState.ExtensionDays.ToString(CultureInfo.InvariantCulture)),
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
            afterFacts.Add(Fact("Payment", "None"));
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

        return new AuditEntryExplanationViewModel(
            "membership-issued",
            "Membership issued with immutable terms",
            "The issue-time terms and stored initial Membership state are shown. Later MembershipType catalog edits do not rewrite this snapshot.",
            "Before issue",
            "Issued Membership",
            beforeFacts,
            afterFacts,
            ChangedFields: "Issued Membership",
            IsAvailable: true);
    }

    private static AuditEntryExplanationViewModel Unavailable(string kind)
    {
        return new AuditEntryExplanationViewModel(
            kind,
            "Readable change summary unavailable",
            "The stored business summary could not be displayed safely.",
            BeforeLabel: string.Empty,
            AfterLabel: string.Empty,
            BeforeFacts: [],
            AfterFacts: [],
            ChangedFields: null,
            IsAvailable: false);
    }

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> PaymentFacts(
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

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> MembershipTypeFacts(
        MembershipTypeCatalogSnapshot membershipType)
    {
        return
        [
            Fact("Name", membershipType.Name),
            Fact(
                "Duration",
                $"{membershipType.DurationDays.ToString(CultureInfo.InvariantCulture)} days"),
            Fact(
                "Visit limit",
                membershipType.VisitsLimit.ToString(CultureInfo.InvariantCulture)),
            Fact(
                "Price",
                MoneyLabel(membershipType.PriceAmount, membershipType.PriceCurrency)),
            Fact("Status", membershipType.IsActive ? "Active" : "Inactive"),
            Fact("Catalog comment", membershipType.Comment ?? "None"),
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

    private static IReadOnlyList<string> MembershipTypeChangedFields(
        MembershipTypeCatalogSnapshot original,
        MembershipTypeCatalogSnapshot updated)
    {
        var changedFields = new List<string>();
        if (original.Name != updated.Name)
        {
            changedFields.Add("Name");
        }

        if (original.DurationDays != updated.DurationDays)
        {
            changedFields.Add("Duration");
        }

        if (original.VisitsLimit != updated.VisitsLimit)
        {
            changedFields.Add("Visit limit");
        }

        if (original.PriceAmount != updated.PriceAmount
            || original.PriceCurrency != updated.PriceCurrency)
        {
            changedFields.Add("Price");
        }

        if (original.Comment != updated.Comment)
        {
            changedFields.Add("Catalog comment");
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

    private static void AddMembershipFacts(
        ICollection<AuditEntryExplanationFactViewModel> facts,
        MembershipStateSnapshot? membershipState)
    {
        if (membershipState is null)
        {
            return;
        }

        facts.Add(Fact(
            "Remaining visits",
            membershipState.RemainingVisits.ToString(CultureInfo.InvariantCulture)));
        facts.Add(Fact(
            "Negative balance",
            membershipState.NegativeBalance.ToString(CultureInfo.InvariantCulture)));
    }

    private static AuditEntryExplanationFactViewModel Fact(string label, string value)
    {
        return new AuditEntryExplanationFactViewModel(label, value);
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

    private static string MoneyLabel(decimal amount, string currency)
    {
        return $"{amount.ToString("0.##", CultureInfo.InvariantCulture)} {currency.ToUpperInvariant()}";
    }

    private static string FreezeRangeLabel(FreezeSnapshot freeze)
    {
        return $"{DateLabel(freeze.StartDate)} to {DateLabel(freeze.EndDate)}";
    }

    private static string DateLabel(DateOnly date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static void ValidateEntryBatch(string entryOrigin, Guid? entryBatchId)
    {
        _ = StoredEntryOriginLabel(entryOrigin);
        if (entryOrigin == "normal" && entryBatchId is not null)
        {
            throw new JsonException("A normal Freeze audit summary cannot reference an entry batch.");
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

    private static string StoredEntryOriginLabel(string entryOrigin)
    {
        return entryOrigin switch
        {
            "normal" => "Normal entry",
            "manual_backfill" => "Manual backfill",
            "paper_fallback" => "Paper fallback",
            "future_import" => "Future import",
            _ => throw new JsonException("Stored entry origin is not supported."),
        };
    }

    private static string OptionalIdLabel(Guid? id)
    {
        return id is { } value ? TimelineModel.ShortId(value) : "No Membership";
    }

    private static string VisitKindLabel(string value)
    {
        return value switch
        {
            "membership" => "Membership visit",
            "one_off" => "One-off visit",
            "trial" => "Trial visit",
            _ => throw new JsonException("Visit kind is not supported."),
        };
    }

    private static string ConsumptionStatusLabel(string? value)
    {
        return value switch
        {
            "active" => "Active",
            "canceled" => "Canceled",
            null => "Not applicable",
            _ => throw new JsonException("Visit consumption status is not supported."),
        };
    }

    private static string PaymentContextLabel(string value)
    {
        return value switch
        {
            "membership_sale" => "Membership sale",
            "one_off" => "One-off",
            "trial" => "Trial",
            "negative_closure" => "Negative closure",
            "other" => "Other",
            _ => throw new JsonException("Payment context is not supported."),
        };
    }

    private static string PaymentMethodLabel(string value)
    {
        return value switch
        {
            "cash" => "Cash",
            _ => throw new JsonException("Payment method is not supported."),
        };
    }

    private static string MembershipStatusLabel(string value)
    {
        return value switch
        {
            "active" => "Active",
            _ => throw new JsonException("Membership status is not supported."),
        };
    }

    private static string MembershipNegativeHandlingLabel(string? value)
    {
        return value switch
        {
            null => "Not required",
            "leave_visible" => "Existing negative balance left visible",
            "cover_with_new_membership" => "Covered by the new Membership",
            "record_explicit_closure" => "Explicit negative closure recorded",
            _ => throw new JsonException(
                "Membership negative handling decision is not supported."),
        };
    }

    private static string StatusLabel(string value)
    {
        return value switch
        {
            "active" => "Active",
            "replaced" => "Replaced",
            "canceled" => "Canceled",
            _ => throw new JsonException("Payment status is not supported."),
        };
    }

    private static string ChangedFieldLabel(string value)
    {
        return value switch
        {
            "amount" => "Amount",
            "currency" => "Currency",
            "occurred_at" => "Occurred time",
            "payment_context" => "Payment context",
            "membership_id" => "Membership",
            "comment" => "Comment",
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

    private sealed record MembershipStateSnapshot(
        int RemainingVisits,
        int NegativeBalance);
}

public sealed record AuditEntryExplanationFactViewModel(
    string Label,
    string Value);
