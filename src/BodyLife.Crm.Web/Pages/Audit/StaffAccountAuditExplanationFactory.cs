using System.Globalization;
using System.Text.Json;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Web.Pages.Audit;

internal static class StaffAccountAuditExplanationFactory
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static AuditEntryExplanationViewModel CreateAccountCreation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var accountId = RequireAccountId(entry.EntityId);
        var created = ReadCreatedAccount(before, after);

        return new AuditEntryExplanationViewModel(
            "staff-account-created",
            "Staff account created",
            "A new active staff account was created with the stored display name and account type. Sign-in credentials are configured separately and are not part of this business summary.",
            "Before creation",
            "Created staff account",
            [Fact("Account state", "Not present")],
            [
                Fact("Staff account", TimelineModel.ShortId(accountId)),
                Fact("Display name", created.DisplayName),
                Fact("Account type", created.AccountTypeLabel),
                Fact("Status", created.IsActive ? "Active" : "Inactive"),
            ],
            ChangedFields: "Staff account",
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateDisplayNameUpdate(
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
            "Staff display name updated",
            displayNameChanged
                ? "The stored snapshots show the staff display-name change. Login and password data are outside this business summary."
                : "The command recorded identical before/after display names. The unchanged stored value remains explicit for audit review, and login/password data are outside this summary.",
            "Original staff profile",
            "Updated staff profile",
            StaffProfileFacts(accountId, originalDisplayName),
            StaffProfileFacts(accountId, updatedDisplayName),
            ChangedFields: displayNameChanged ? "Display name" : null,
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateActivation(
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
            "Staff account activated",
            "The stored account state changed from Inactive to Active. Credential values are not part of this lifecycle summary.",
            "Before activation",
            "After activation",
            StaffStateFacts(accountId, original.IsActive),
            StaffStateFacts(accountId, activated.IsActive),
            ChangedFields: "Account status",
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateDeactivation(
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
            "Staff account deactivated",
            "The account is now Inactive. The stored ended-session count explains the immediate access impact without exposing credential material.",
            "Before deactivation",
            "After deactivation",
            StaffStateFacts(accountId, original.IsActive),
            [
                .. StaffStateFacts(accountId, deactivated.IsActive),
                Fact(
                    "Active sessions ended",
                    deactivated.EndedSessionCount.ToString(CultureInfo.InvariantCulture)),
            ],
            ChangedFields: deactivated.EndedSessionCount > 0
                ? "Account status, Active sessions"
                : "Account status",
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateCredentialConfiguration(
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
            "Staff credentials configured",
            "The staff account now has configured sign-in credentials. Audit stores only credential state and session impact; login names, passwords and hashes are not included.",
            "Before configuration",
            "After configuration",
            CredentialFacts(accountId, original.CredentialsConfigured),
            [
                .. CredentialFacts(accountId, configured.CredentialsConfigured),
                Fact(
                    "Active sessions ended",
                    configured.EndedSessionCount.ToString(CultureInfo.InvariantCulture)),
            ],
            ChangedFields: configured.EndedSessionCount > 0
                ? "Credential state, Active sessions"
                : "Credential state",
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateCredentialReset(
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
            "Staff credentials reset",
            "The configured credentials were replaced and active sessions were ended as recorded. Login names, passwords and hashes are deliberately absent from business audit.",
            "Before reset",
            "After reset",
            CredentialFacts(accountId, original.CredentialsConfigured),
            [
                .. CredentialFacts(accountId, reset.CredentialsConfigured),
                Fact(
                    "Active sessions ended",
                    reset.EndedSessionCount.ToString(CultureInfo.InvariantCulture)),
            ],
            ChangedFields: reset.EndedSessionCount > 0
                ? "Credentials, Active sessions"
                : "Credentials",
            IsAvailable: true);
    }

    private static string ReadDisplayName(JsonElement summary)
    {
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
            || before.EnumerateObject().Any()
            || after.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Staff account creation summary is inconsistent.");
        }

        var properties = after
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        if (properties.Length != 4
            || !properties.Contains("displayName", StringComparer.Ordinal)
            || !properties.Contains("accountType", StringComparer.Ordinal)
            || !properties.Contains("role", StringComparer.Ordinal)
            || !properties.Contains("isActive", StringComparer.Ordinal))
        {
            throw new JsonException("Staff account creation fields are inconsistent.");
        }

        var dto = Deserialize<CreatedAccountDto>(after);
        var accountTypeLabel = dto.AccountType switch
        {
            "named_admin" => "Named Admin",
            "shared_reception_admin" => "Shared Reception/Admin",
            _ => throw new JsonException("Staff account type is not manageable."),
        };
        if (string.IsNullOrWhiteSpace(dto.DisplayName)
            || dto.Role != "admin"
            || dto.IsActive != true)
        {
            throw new JsonException("Created staff account state is inconsistent.");
        }

        return new CreatedAccountSnapshot(
            dto.DisplayName,
            accountTypeLabel,
            dto.IsActive.Value);
    }

    private static ActiveStateSnapshot ReadActiveState(
        JsonElement summary,
        bool requireEndedSessionCount)
    {
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

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> StaffProfileFacts(
        Guid accountId,
        string displayName)
    {
        return
        [
            Fact("Staff account", TimelineModel.ShortId(accountId)),
            Fact("Display name", displayName),
        ];
    }

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> StaffStateFacts(
        Guid accountId,
        bool isActive)
    {
        return
        [
            Fact("Staff account", TimelineModel.ShortId(accountId)),
            Fact("Status", isActive ? "Active" : "Inactive"),
        ];
    }

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> CredentialFacts(
        Guid accountId,
        bool credentialsConfigured)
    {
        return
        [
            Fact("Staff account", TimelineModel.ShortId(accountId)),
            Fact(
                "Credential state",
                credentialsConfigured ? "Configured" : "Not configured"),
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

    private static AuditEntryExplanationFactViewModel Fact(string label, string value)
    {
        return new AuditEntryExplanationFactViewModel(label, value);
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
        string AccountTypeLabel,
        bool IsActive);

    private sealed record CredentialStateSnapshot(
        bool CredentialsConfigured,
        int EndedSessionCount);
}
