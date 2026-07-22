using System.Text.Json;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class StaffAccountAuditExplanationFactory(AuditPresentation presentation)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal AuditEntryExplanationViewModel CreateAccountCreation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var created = ReadCreatedAccount(before, after);

        return new AuditEntryExplanationViewModel(
            "staff-account-created",
            presentation.Explanation("StaffAccountCreated.Title"),
            presentation.Explanation("StaffAccountCreated.Narrative"),
            presentation.Explanation("StaffAccountCreated.Before"),
            presentation.Explanation("StaffAccountCreated.After"),
            [Fact("Account state", presentation.Value("NotPresent"))],
            [
                Fact("Staff account", presentation.ShortId(accountId)),
                Fact("Display name", created.DisplayName),
                Fact("Account type", AccountTypeLabel(created.AccountType)),
                Fact("Status", StatusLabel(created.IsActive)),
            ],
            ChangedFields: presentation.Changed("StaffAccount"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateDisplayNameUpdate(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var originalDisplayName = ReadDisplayName(before);
        var updatedDisplayName = ReadDisplayName(after);
        var displayNameChanged = originalDisplayName != updatedDisplayName;

        return new AuditEntryExplanationViewModel(
            "staff-account-display-name-updated",
            presentation.Explanation("StaffDisplayNameUpdated.Title"),
            displayNameChanged
                ? presentation.Explanation("StaffDisplayNameUpdated.ChangedNarrative")
                : presentation.Explanation("StaffDisplayNameUpdated.UnchangedNarrative"),
            presentation.Explanation("StaffDisplayNameUpdated.Before"),
            presentation.Explanation("StaffDisplayNameUpdated.After"),
            StaffProfileFacts(accountId, originalDisplayName),
            StaffProfileFacts(accountId, updatedDisplayName),
            ChangedFields: displayNameChanged ? presentation.Changed("DisplayName") : null,
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateActivation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var original = ReadActiveState(before, requireEndedSessionCount: false);
        var activated = ReadActiveState(after, requireEndedSessionCount: true);

        if (original.IsActive
            || !activated.IsActive
            || activated.EndedSessionCount != 0)
        {
            throw new JsonException("Staff activation summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "staff-account-activated",
            presentation.Explanation("StaffAccountActivated.Title"),
            presentation.Explanation("StaffAccountActivated.Narrative"),
            presentation.Explanation("StaffAccountActivated.Before"),
            presentation.Explanation("StaffAccountActivated.After"),
            StaffStateFacts(accountId, original.IsActive),
            StaffStateFacts(accountId, activated.IsActive),
            ChangedFields: presentation.Changed("AccountStatus"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateDeactivation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var original = ReadActiveState(before, requireEndedSessionCount: false);
        var deactivated = ReadActiveState(after, requireEndedSessionCount: true);

        if (!original.IsActive
            || deactivated.IsActive
            || string.IsNullOrWhiteSpace(entry.Reason))
        {
            throw new JsonException("Staff deactivation summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "staff-account-deactivated",
            presentation.Explanation("StaffAccountDeactivated.Title"),
            presentation.Explanation("StaffAccountDeactivated.Narrative"),
            presentation.Explanation("StaffAccountDeactivated.Before"),
            presentation.Explanation("StaffAccountDeactivated.After"),
            StaffStateFacts(accountId, original.IsActive),
            [
                .. StaffStateFacts(accountId, deactivated.IsActive),
                Fact(
                    "Active sessions ended",
                    presentation.Number(deactivated.EndedSessionCount)),
            ],
            ChangedFields: deactivated.EndedSessionCount > 0
                ? JoinChanged("AccountStatus", "ActiveSessions")
                : presentation.Changed("AccountStatus"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCredentialConfiguration(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var original = ReadCredentialState(before, requireEndedSessionCount: false);
        var configured = ReadCredentialState(after, requireEndedSessionCount: true);

        if (original.CredentialsConfigured || !configured.CredentialsConfigured)
        {
            throw new JsonException("Staff credential configuration summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "staff-credentials-configured",
            presentation.Explanation("StaffCredentialsConfigured.Title"),
            presentation.Explanation("StaffCredentialsConfigured.Narrative"),
            presentation.Explanation("StaffCredentialsConfigured.Before"),
            presentation.Explanation("StaffCredentialsConfigured.After"),
            CredentialFacts(accountId, original.CredentialsConfigured),
            [
                .. CredentialFacts(accountId, configured.CredentialsConfigured),
                Fact(
                    "Active sessions ended",
                    presentation.Number(configured.EndedSessionCount)),
            ],
            ChangedFields: configured.EndedSessionCount > 0
                ? JoinChanged("CredentialState", "ActiveSessions")
                : presentation.Changed("CredentialState"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCredentialReset(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var original = ReadCredentialState(before, requireEndedSessionCount: false);
        var reset = ReadCredentialState(after, requireEndedSessionCount: true);

        if (!original.CredentialsConfigured
            || !reset.CredentialsConfigured
            || string.IsNullOrWhiteSpace(entry.Reason))
        {
            throw new JsonException("Staff credential reset summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "staff-credentials-reset",
            presentation.Explanation("StaffCredentialsReset.Title"),
            presentation.Explanation("StaffCredentialsReset.Narrative"),
            presentation.Explanation("StaffCredentialsReset.Before"),
            presentation.Explanation("StaffCredentialsReset.After"),
            CredentialFacts(accountId, original.CredentialsConfigured),
            [
                .. CredentialFacts(accountId, reset.CredentialsConfigured),
                Fact(
                    "Active sessions ended",
                    presentation.Number(reset.EndedSessionCount)),
            ],
            ChangedFields: reset.EndedSessionCount > 0
                ? JoinChanged("Credentials", "ActiveSessions")
                : presentation.Changed("Credentials"),
            IsAvailable: true);
    }

    private static string ReadDisplayName(JsonElement summary)
    {
        RequireExactProperties(summary, "displayName");
        var dto = Deserialize<DisplayNameDto>(summary);
        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            throw new JsonException("Staff display name is required.");
        }

        return dto.DisplayName;
    }

    private static CreatedAccountSnapshot ReadCreatedAccount(
        JsonElement before,
        JsonElement after)
    {
        if (before.ValueKind != JsonValueKind.Object
            || before.EnumerateObject().Any())
        {
            throw new JsonException("Staff account creation summary is inconsistent.");
        }

        RequireExactProperties(
            after,
            "displayName",
            "accountType",
            "role",
            "isActive");

        var dto = Deserialize<CreatedAccountDto>(after);
        if (dto.AccountType is not ("named_admin" or "shared_reception_admin"))
        {
            throw new JsonException("Staff account type is not manageable.");
        }
        if (string.IsNullOrWhiteSpace(dto.DisplayName)
            || dto.Role != "admin"
            || dto.IsActive != true)
        {
            throw new JsonException("Created staff account state is inconsistent.");
        }

        return new CreatedAccountSnapshot(
            dto.DisplayName,
            dto.AccountType,
            dto.IsActive.Value);
    }

    private static ActiveStateSnapshot ReadActiveState(
        JsonElement summary,
        bool requireEndedSessionCount)
    {
        RequireExactProperties(
            summary,
            requireEndedSessionCount
                ? ["isActive", "endedSessionCount"]
                : ["isActive"]);
        var dto = Deserialize<ActiveStateDto>(summary);
        if (dto.IsActive is null
            || (requireEndedSessionCount && dto.EndedSessionCount is null)
            || (!requireEndedSessionCount && dto.EndedSessionCount is not null)
            || dto.EndedSessionCount < 0)
        {
            throw new JsonException("Staff active-state summary is inconsistent.");
        }

        return new ActiveStateSnapshot(
            dto.IsActive.Value,
            dto.EndedSessionCount ?? 0);
    }

    private static CredentialStateSnapshot ReadCredentialState(
        JsonElement summary,
        bool requireEndedSessionCount)
    {
        RequireExactProperties(
            summary,
            requireEndedSessionCount
                ? ["credentialsConfigured", "endedSessionCount"]
                : ["credentialsConfigured"]);
        var dto = Deserialize<CredentialStateDto>(summary);
        if (dto.CredentialsConfigured is null
            || (requireEndedSessionCount && dto.EndedSessionCount is null)
            || (!requireEndedSessionCount && dto.EndedSessionCount is not null)
            || dto.EndedSessionCount < 0)
        {
            throw new JsonException("Staff credential-state summary is inconsistent.");
        }

        return new CredentialStateSnapshot(
            dto.CredentialsConfigured.Value,
            dto.EndedSessionCount ?? 0);
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> StaffProfileFacts(
        Guid accountId,
        string displayName)
    {
        return
        [
            Fact("Staff account", presentation.ShortId(accountId)),
            Fact("Display name", displayName),
        ];
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> StaffStateFacts(
        Guid accountId,
        bool isActive)
    {
        return
        [
            Fact("Staff account", presentation.ShortId(accountId)),
            Fact("Status", StatusLabel(isActive)),
        ];
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> CredentialFacts(
        Guid accountId,
        bool credentialsConfigured)
    {
        return
        [
            Fact("Staff account", presentation.ShortId(accountId)),
            Fact(
                "Credential state",
                credentialsConfigured
                    ? presentation.Text("CredentialState.Configured")
                    : presentation.Text("CredentialState.NotConfigured")),
        ];
    }

    private static Guid RequireAccountId(Guid accountId)
    {
        return accountId != Guid.Empty
            ? accountId
            : throw new JsonException("Staff account id is required.");
    }

    private static T Deserialize<T>(JsonElement element)
    {
        return element.Deserialize<T>(AuditJsonOptions)
            ?? throw new JsonException("The staff audit summary is required.");
    }

    private static void RequireExactProperties(
        JsonElement summary,
        params string[] expectedProperties)
    {
        if (summary.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Staff audit summary must be an object.");
        }

        var actualProperties = summary
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (actualProperties.Length != expectedProperties.Length
            || actualProperties.Distinct(StringComparer.Ordinal).Count()
                != actualProperties.Length
            || !expectedProperties.All(expected =>
                actualProperties.Contains(expected, StringComparer.Ordinal)))
        {
            throw new JsonException(
                "Staff audit summary fields are inconsistent.");
        }
    }

    private string AccountTypeLabel(string accountType) => accountType switch
    {
        "named_admin" => presentation.Text("AccountType.NamedAdmin"),
        "shared_reception_admin" => presentation.Text("AccountType.SharedReceptionAdmin"),
        _ => throw new JsonException("Staff account type is not manageable."),
    };

    private string StatusLabel(bool isActive) =>
        presentation.Status(isActive ? "Active" : "Inactive");

    private string JoinChanged(params string[] keys) =>
        string.Join(", ", keys.Select(presentation.Changed));

    private AuditEntryExplanationFactViewModel Fact(string label, string value)
    {
        var semanticKey = label switch
        {
            "Account state" => "AccountState",
            "Staff account" => "StaffAccount",
            "Display name" => "DisplayName",
            "Account type" => "AccountType",
            "Status" => "Status",
            "Active sessions ended" => "ActiveSessionsEnded",
            "Credential state" => "CredentialState",
            _ => throw new InvalidOperationException($"Unsupported staff audit explanation fact label '{label}'."),
        };
        return new AuditEntryExplanationFactViewModel(presentation.Fact(semanticKey), value);
    }

    private sealed class DisplayNameDto
    {
        public string? DisplayName { get; init; }
    }

    private sealed class CreatedAccountDto
    {
        public string? DisplayName { get; init; }

        public string? AccountType { get; init; }

        public string? Role { get; init; }

        public bool? IsActive { get; init; }
    }

    private sealed class ActiveStateDto
    {
        public bool? IsActive { get; init; }

        public int? EndedSessionCount { get; init; }
    }

    private sealed class CredentialStateDto
    {
        public bool? CredentialsConfigured { get; init; }

        public int? EndedSessionCount { get; init; }
    }

    private sealed record ActiveStateSnapshot(
        bool IsActive,
        int EndedSessionCount);

    private sealed record CreatedAccountSnapshot(
        string DisplayName,
        string AccountType,
        bool IsActive);

    private sealed record CredentialStateSnapshot(
        bool CredentialsConfigured,
        int EndedSessionCount);
}
