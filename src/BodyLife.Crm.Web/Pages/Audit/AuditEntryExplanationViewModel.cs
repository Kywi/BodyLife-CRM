using System.Globalization;
using System.Text.Json;
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
            using var before = JsonDocument.Parse(entry.BeforeSummaryJson);
            using var after = JsonDocument.Parse(entry.AfterSummaryJson);
            return entry.ActionType switch
            {
                "membership_type.edited"
                    when entry.EntityType == AuditTimelineEntityType.MembershipType
                    => CreateMembershipTypeEdit(before.RootElement, after.RootElement),
                "membership_type.deactivated"
                    when entry.EntityType == AuditTimelineEntityType.MembershipType
                    => CreateMembershipTypeDeactivation(before.RootElement, after.RootElement),
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
