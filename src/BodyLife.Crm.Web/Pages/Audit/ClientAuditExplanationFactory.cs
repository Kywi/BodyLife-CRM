using System.Text.Json;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class ClientAuditExplanationFactory(AuditPresentation presentation)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal AuditEntryExplanationViewModel CreateClientCreation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        if (before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any()
            || !related.TryGetProperty("cardAssignmentId", out _)
            || !after.TryGetProperty("patronymic", out _)
            || !after.TryGetProperty("phone", out _)
            || !after.TryGetProperty("comment", out _)
            || !after.TryGetProperty("cardNumber", out _))
        {
            throw new JsonException("Client creation summary shape is inconsistent.");
        }

        var relatedDto = Deserialize<ClientCreationRelatedDto>(related);
        var createdDto = Deserialize<CreatedClientDto>(after);
        var created = ReadCreatedClient(createdDto);
        var acknowledgements = ReadAcknowledgements(
            createdDto.DuplicateWarningAcknowledgements);
        var acknowledgementIds = ReadDistinctIds(
            relatedDto.DuplicateWarningAcknowledgementIds,
            "duplicateWarningAcknowledgementIds");
        var matchedClientIds = ReadDistinctIds(
            relatedDto.MatchedClientIds,
            "matchedClientIds");
        var acknowledgedClientIds = acknowledgements
            .Select(acknowledgement => acknowledgement.MatchedClientId)
            .ToHashSet();
        var cardAssignmentId = relatedDto.CardAssignmentId;

        if (entry.EntityId == Guid.Empty
            || cardAssignmentId == Guid.Empty
            || (cardAssignmentId is null) != (created.CardNumber is null)
            || acknowledgementIds.Count != acknowledgements.Count
            || !matchedClientIds.SetEquals(acknowledgedClientIds))
        {
            throw new JsonException("Client creation summary is inconsistent.");
        }

        List<AuditEntryExplanationFactViewModel> afterFacts =
        [
            Fact("Client", presentation.ShortId(entry.EntityId)),
            Fact("Name", FullName(created)),
            Fact("Phone", created.Phone ?? presentation.Value("None")),
            Fact("Operational status", StatusLabel(created.OperationalStatus)),
            Fact("Comment", created.Comment ?? presentation.Value("None")),
            Fact("Current card", created.CardNumber ?? presentation.Value("None")),
            Fact(
                "Card assignment",
                cardAssignmentId is { } assignmentId
                    ? presentation.ShortId(assignmentId)
                    : presentation.Value("None")),
            Fact(
                "Warnings acknowledged",
                presentation.Number(acknowledgements.Count)),
        ];
        if (acknowledgements.Count > 0)
        {
            afterFacts.Add(Fact(
                "Acknowledgement details",
                string.Join("; ", acknowledgements.Select(AcknowledgementLabel))));
        }

        return new AuditEntryExplanationViewModel(
            "client-created",
            presentation.Explanation("ClientCreated.Title"),
            presentation.Explanation("ClientCreated.Narrative"),
            presentation.Explanation("ClientCreated.Before"),
            presentation.Explanation("ClientCreated.After"),
            [
                Fact("Client", presentation.Value("NotPresent")),
                Fact("Current card", presentation.Value("None")),
                Fact("Warnings acknowledged", presentation.Number(0)),
            ],
            afterFacts,
            ChangedFields: presentation.Changed("ClientProfile"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateClientUpdate(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var relatedDto = Deserialize<ClientUpdateRelatedDto>(related);
        var original = ReadClientIdentity(Deserialize<ClientIdentityDto>(before));
        var updatedDto = Deserialize<UpdatedClientIdentityDto>(after);
        var updated = ReadClientIdentity(updatedDto);
        var acknowledgements = ReadAcknowledgements(
            updatedDto.DuplicateWarningAcknowledgements);
        var acknowledgementIds = ReadDistinctIds(
            relatedDto.DuplicateWarningAcknowledgementIds,
            "duplicateWarningAcknowledgementIds");
        var matchedClientIds = ReadDistinctIds(
            relatedDto.MatchedClientIds,
            "matchedClientIds");
        var acknowledgedClientIds = acknowledgements
            .Select(acknowledgement => acknowledgement.MatchedClientId)
            .ToHashSet();

        if (updated.UpdatedAt <= original.UpdatedAt
            || acknowledgementIds.Count != acknowledgements.Count
            || !matchedClientIds.SetEquals(acknowledgedClientIds))
        {
            throw new JsonException("Client update summary is inconsistent.");
        }

        var changedFields = ClientChangedFields(original, updated);
        if (acknowledgements.Count > 0)
        {
            changedFields.Add("DuplicateWarningsAcknowledged");
        }

        if (changedFields.Count == 0)
        {
            throw new JsonException("Client update did not change business values.");
        }

        var afterFacts = ClientFacts(updated).ToList();
        if (acknowledgements.Count > 0)
        {
            afterFacts.Add(Fact(
                "Warnings acknowledged",
                presentation.Number(acknowledgements.Count)));
            afterFacts.Add(Fact(
                "Acknowledgement details",
                string.Join("; ", acknowledgements.Select(AcknowledgementLabel))));
        }

        return new AuditEntryExplanationViewModel(
            "client-updated",
            presentation.Explanation("ClientUpdated.Title"),
            presentation.Explanation("ClientUpdated.Narrative"),
            presentation.Explanation("ClientUpdated.Before"),
            presentation.Explanation("ClientUpdated.After"),
            ClientFacts(original),
            afterFacts,
            string.Join(", ", changedFields.Select(presentation.Changed)),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCardAssignment(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var refs = ReadCardReferences(related);
        var original = ReadCardAssignment(before);
        var current = ReadCardAssignment(after);

        if (original is not null
            || current is null
            || refs.PreviousCardAssignmentId is not null
            || refs.CurrentCardAssignmentId != current.Id
            || current.AssignedAt != entry.OccurredAt)
        {
            throw new JsonException("Card assignment summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "card-assigned",
            presentation.Explanation("CardAssigned.Title"),
            presentation.Explanation("CardAssigned.Narrative"),
            presentation.Explanation("CardAssigned.Before"),
            presentation.Explanation("CardAssigned.After"),
            [Fact("Current card", presentation.Value("None"))],
            CardFacts(current),
            ChangedFields: presentation.Changed("CurrentCard"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCardChange(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var refs = ReadCardReferences(related);
        var original = ReadCardAssignment(before);
        var current = ReadCardAssignment(after);

        if (original is null
            || current is null
            || refs.PreviousCardAssignmentId != original.Id
            || refs.CurrentCardAssignmentId != current.Id
            || original.Id == current.Id
            || original.AssignedAt > entry.OccurredAt
            || current.AssignedAt != entry.OccurredAt
            || !HasReasonOrComment(entry))
        {
            throw new JsonException("Card change summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "card-changed",
            presentation.Explanation("CardChanged.Title"),
            presentation.Explanation("CardChanged.Narrative"),
            presentation.Explanation("CardChanged.Before"),
            presentation.Explanation("CardChanged.After"),
            CardFacts(original),
            CardFacts(current),
            ChangedFields: original.CardNumberNormalized == current.CardNumberNormalized
                ? presentation.Changed("CardAssignment")
                : string.Join(", ", [presentation.Changed("CardNumber"), presentation.Changed("CardAssignment")]),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCardClear(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var refs = ReadCardReferences(related);
        var original = ReadCardAssignment(before);
        var current = ReadCardAssignment(after);

        if (original is null
            || current is not null
            || refs.PreviousCardAssignmentId != original.Id
            || refs.CurrentCardAssignmentId is not null
            || original.AssignedAt > entry.OccurredAt
            || !HasReasonOrComment(entry))
        {
            throw new JsonException("Card clear summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "card-cleared",
            presentation.Explanation("CardCleared.Title"),
            presentation.Explanation("CardCleared.Narrative"),
            presentation.Explanation("CardCleared.Before"),
            presentation.Explanation("CardCleared.After"),
            CardFacts(original),
            [
                Fact("Previous assignment", presentation.Value("PreservedInHistory")),
                Fact("Current card", presentation.Value("None")),
            ],
            ChangedFields: presentation.Changed("CurrentCard"),
            IsAvailable: true);
    }

    private static ClientIdentitySnapshot ReadClientIdentity(ClientIdentityDto identity)
    {
        var status = RequireText(identity.OperationalStatus, "operationalStatus");
        if (status is not ("active" or "inactive"))
        {
            throw new JsonException("Client operational status is invalid.");
        }

        return new ClientIdentitySnapshot(
            RequireText(identity.Surname, "surname"),
            RequireText(identity.Name, "name"),
            ReadOptionalText(identity.Patronymic, "patronymic"),
            ReadOptionalText(identity.Phone, "phone"),
            status,
            ReadOptionalText(identity.Comment, "comment"),
            RequireTimestamp(identity.UpdatedAt, "updatedAt"));
    }

    private static ClientCreationSnapshot ReadCreatedClient(CreatedClientDto client)
    {
        var status = RequireText(client.OperationalStatus, "operationalStatus");
        if (status is not ("active" or "inactive"))
        {
            throw new JsonException("Client operational status is invalid.");
        }

        return new ClientCreationSnapshot(
            RequireText(client.Surname, "surname"),
            RequireText(client.Name, "name"),
            ReadOptionalText(client.Patronymic, "patronymic"),
            ReadOptionalText(client.Phone, "phone"),
            status,
            ReadOptionalText(client.Comment, "comment"),
            ReadOptionalText(client.CardNumber, "cardNumber"));
    }

    private static IReadOnlyList<DuplicateWarningAcknowledgementSnapshot>
        ReadAcknowledgements(
            IReadOnlyList<DuplicateWarningAcknowledgementDto?>? acknowledgements)
    {
        acknowledgements = acknowledgements
            ?? throw new JsonException("Duplicate warning acknowledgements are required.");
        var result = new List<DuplicateWarningAcknowledgementSnapshot>(
            acknowledgements.Count);
        var keys = new HashSet<(Guid MatchedClientId, string WarningType)>();

        foreach (var acknowledgement in acknowledgements)
        {
            if (acknowledgement is null)
            {
                throw new JsonException("A duplicate warning acknowledgement is required.");
            }

            var warningType = RequireText(acknowledgement.WarningType, "warningType");
            var matchedClientId = RequireId(
                acknowledgement.MatchedClientId,
                "matchedClientId");
            if (warningType is not ("duplicate_phone" or "similar_name")
                || !keys.Add((matchedClientId, warningType)))
            {
                throw new JsonException(
                    "Duplicate warning acknowledgement is inconsistent.");
            }

            result.Add(new DuplicateWarningAcknowledgementSnapshot(
                warningType,
                matchedClientId,
                RequireText(acknowledgement.Reason, "reason")));
        }

        return result;
    }

    private static HashSet<Guid> ReadDistinctIds(
        IReadOnlyList<Guid>? ids,
        string propertyName)
    {
        if (ids is null)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is required.");
        }
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Count)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is inconsistent.");
        }

        return ids.ToHashSet();
    }

    private static List<string> ClientChangedFields(
        ClientIdentitySnapshot original,
        ClientIdentitySnapshot updated)
    {
        var fields = new List<string>();
        AddChangedField(
            fields,
            original.NameValues() != updated.NameValues(),
            "Name");
        AddChangedField(fields, original.Phone != updated.Phone, "Phone");
        AddChangedField(
            fields,
            original.OperationalStatus != updated.OperationalStatus,
            "OperationalStatus");
        AddChangedField(fields, original.Comment != updated.Comment, "Comment");
        return fields;
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> ClientFacts(
        ClientIdentitySnapshot client)
    {
        return
        [
            Fact("Name", FullName(client)),
            Fact("Phone", client.Phone ?? presentation.Value("None")),
            Fact("Operational status", StatusLabel(client.OperationalStatus)),
            Fact("Comment", client.Comment ?? presentation.Value("None")),
            Fact("Updated", presentation.Timestamp(client.UpdatedAt)),
        ];
    }

    private static CardReferencesSnapshot ReadCardReferences(JsonElement related)
    {
        if (!related.TryGetProperty("previousCardAssignmentId", out var previous)
            || !related.TryGetProperty("currentCardAssignmentId", out var current))
        {
            throw new JsonException("Card assignment references are required.");
        }

        return new CardReferencesSnapshot(
            ReadNullableId(previous, "previousCardAssignmentId"),
            ReadNullableId(current, "currentCardAssignmentId"));
    }

    private static CardAssignmentSnapshot? ReadCardAssignment(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Card assignment summary must be an object.");
        }

        if (!element.EnumerateObject().Any())
        {
            return null;
        }

        var assignment = Deserialize<CardAssignmentDto>(element);
        return new CardAssignmentSnapshot(
            RequireId(assignment.Id, "id"),
            RequireText(assignment.CardNumber, "cardNumber"),
            RequireText(assignment.CardNumberNormalized, "cardNumberNormalized"),
            RequireTimestamp(assignment.AssignedAt, "assignedAt"));
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> CardFacts(
        CardAssignmentSnapshot assignment)
    {
        return
        [
            Fact("Card number", assignment.CardNumber),
            Fact("Assigned", presentation.Timestamp(assignment.AssignedAt)),
            Fact("Assignment", presentation.ShortId(assignment.Id)),
        ];
    }

    private string AcknowledgementLabel(
        DuplicateWarningAcknowledgementSnapshot acknowledgement)
    {
        var warning = acknowledgement.WarningType switch
        {
            "duplicate_phone" => presentation.Text("Warning.DuplicatePhone"),
            "similar_name" => presentation.Text("Warning.SimilarName"),
            _ => throw new JsonException("Duplicate warning type is invalid."),
        };
        return presentation.Text(
            "Template.WarningAcknowledgement",
            warning,
            presentation.ShortId(acknowledgement.MatchedClientId),
            acknowledgement.Reason);
    }

    private static string FullName(ClientIdentitySnapshot client)
    {
        return string.Join(
            " ",
            new[] { client.Surname, client.Name, client.Patronymic }
                .Where(part => part is not null));
    }

    private static string FullName(ClientCreationSnapshot client)
    {
        return string.Join(
            " ",
            new[] { client.Surname, client.Name, client.Patronymic }
                .Where(part => part is not null));
    }

    private string StatusLabel(string status)
    {
        return status switch
        {
            "active" => presentation.Status("Active"),
            "inactive" => presentation.Status("Inactive"),
            _ => throw new JsonException("Client operational status is invalid."),
        };
    }

    private static bool HasReasonOrComment(AuditTimelineEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Reason)
            || !string.IsNullOrWhiteSpace(entry.Comment);
    }

    private static T Deserialize<T>(JsonElement element)
    {
        return element.Deserialize<T>(AuditJsonOptions)
            ?? throw new JsonException("The audit summary is required.");
    }

    private static Guid RequireId(Guid value, string propertyName)
    {
        return value != Guid.Empty
            ? value
            : throw new JsonException(
                $"Audit summary property '{propertyName}' is required.");
    }

    private static Guid? ReadNullableId(JsonElement value, string propertyName)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String
            || !value.TryGetGuid(out var id)
            || id == Guid.Empty)
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is invalid.");
        }

        return id;
    }

    private static string RequireText(string? value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is required.");
        }

        return value;
    }

    private static string? ReadOptionalText(string? value, string propertyName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is invalid.");
        }

        return value;
    }

    private static DateTimeOffset RequireTimestamp(
        DateTimeOffset value,
        string propertyName)
    {
        return value != default
            ? value
            : throw new JsonException(
                $"Audit summary property '{propertyName}' is required.");
    }

    private static void AddChangedField(
        ICollection<string> fields,
        bool changed,
        string label)
    {
        if (changed)
        {
            fields.Add(label);
        }
    }

    private AuditEntryExplanationFactViewModel Fact(string label, string value)
    {
        var semanticKey = label switch
        {
            "Client" => "Client",
            "Name" => "ClientName",
            "Phone" => "Phone",
            "Operational status" => "OperationalStatus",
            "Comment" => "Comment",
            "Current card" => "CurrentCard",
            "Card assignment" => "CardAssignment",
            "Warnings acknowledged" => "WarningsAcknowledged",
            "Acknowledgement details" => "AcknowledgementDetails",
            "Updated" => "Updated",
            "Card number" => "CardNumber",
            "Assigned" => "Assigned",
            "Assignment" => "Assignment",
            "Previous assignment" => "PreviousAssignment",
            _ => throw new InvalidOperationException($"Unsupported Client audit explanation fact label '{label}'."),
        };
        return new AuditEntryExplanationFactViewModel(presentation.Fact(semanticKey), value);
    }

    private sealed class ClientUpdateRelatedDto
    {
        public IReadOnlyList<Guid>? DuplicateWarningAcknowledgementIds { get; init; }

        public IReadOnlyList<Guid>? MatchedClientIds { get; init; }
    }

    private sealed class ClientCreationRelatedDto
    {
        public Guid? CardAssignmentId { get; init; }

        public IReadOnlyList<Guid>? DuplicateWarningAcknowledgementIds { get; init; }

        public IReadOnlyList<Guid>? MatchedClientIds { get; init; }
    }

    private class ClientIdentityDto
    {
        public string? Surname { get; init; }

        public string? Name { get; init; }

        public string? Patronymic { get; init; }

        public string? Phone { get; init; }

        public string? OperationalStatus { get; init; }

        public string? Comment { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class UpdatedClientIdentityDto : ClientIdentityDto
    {
        public DuplicateWarningAcknowledgementDto?[]?
            DuplicateWarningAcknowledgements
        { get; init; }
    }

    private sealed class CreatedClientDto
    {
        public string? Surname { get; init; }

        public string? Name { get; init; }

        public string? Patronymic { get; init; }

        public string? Phone { get; init; }

        public string? OperationalStatus { get; init; }

        public string? Comment { get; init; }

        public string? CardNumber { get; init; }

        public DuplicateWarningAcknowledgementDto?[]?
            DuplicateWarningAcknowledgements
        { get; init; }
    }

    private sealed class DuplicateWarningAcknowledgementDto
    {
        public string? WarningType { get; init; }

        public Guid MatchedClientId { get; init; }

        public string? Reason { get; init; }
    }

    private sealed class CardAssignmentDto
    {
        public Guid Id { get; init; }

        public string? CardNumber { get; init; }

        public string? CardNumberNormalized { get; init; }

        public DateTimeOffset AssignedAt { get; init; }
    }

    private sealed record ClientIdentitySnapshot(
        string Surname,
        string Name,
        string? Patronymic,
        string? Phone,
        string OperationalStatus,
        string? Comment,
        DateTimeOffset UpdatedAt)
    {
        internal (string Surname, string Name, string? Patronymic) NameValues()
        {
            return (Surname, Name, Patronymic);
        }
    }

    private sealed record ClientCreationSnapshot(
        string Surname,
        string Name,
        string? Patronymic,
        string? Phone,
        string OperationalStatus,
        string? Comment,
        string? CardNumber);

    private sealed record DuplicateWarningAcknowledgementSnapshot(
        string WarningType,
        Guid MatchedClientId,
        string Reason);

    private sealed record CardReferencesSnapshot(
        Guid? PreviousCardAssignmentId,
        Guid? CurrentCardAssignmentId);

    private sealed record CardAssignmentSnapshot(
        Guid Id,
        string CardNumber,
        string CardNumberNormalized,
        DateTimeOffset AssignedAt);
}
