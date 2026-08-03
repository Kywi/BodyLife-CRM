using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Web.Pages.Reception;

/// <summary>Presentation-only adapter for a Memberships-owned issued-sale correction preview.</summary>
public sealed record IssuedMembershipSaleCorrectionFormViewModel(
    IssuedMembershipSaleCorrectionFormInput Input,
    IReadOnlyList<MembershipTypeCatalogItem> ActiveOrdinaryTypes,
    PreviewIssuedMembershipSaleCorrectionResult PreviewResult,
    IReadOnlyList<CommandError> Errors)
{
    public bool IsUnavailable => PreviewResult.Status
            != PreviewIssuedMembershipSaleCorrectionStatus.Success
        || PreviewResult.Preview is null;

    public IssuedMembershipSaleCorrectionPreview? Preview => PreviewResult.Preview;

    public bool HasDependencies => Preview?.Dependencies.Count > 0;

    public bool HasConfirmedPreview => PreviewResult.Status == PreviewIssuedMembershipSaleCorrectionStatus.Success
        && Preview is not null
        && !HasDependencies
        && !string.IsNullOrWhiteSpace(Input.ExpectedDependencyToken)
        && (Input.Mode switch
        {
            IssuedMembershipSaleCorrectionMode.Cancel => Preview.Replacement is null,
            IssuedMembershipSaleCorrectionMode.Replace => Preview.Replacement is { } replacement
                && replacement.MembershipTypeId == Input.ReplacementMembershipTypeId
                && replacement.ExpectedMembershipTypeUpdatedAt
                    == Input.ExpectedMembershipTypeUpdatedAt
                && replacement.StartDate == Input.ReplacementStartDate,
            _ => false,
        });

    public bool CanSubmit => HasConfirmedPreview
        && !string.IsNullOrWhiteSpace(Input.Reason)
        && Input.OccurredAtLocal is not null
        && Input.Confirmed;

    public static IssuedMembershipSaleCorrectionFormViewModel Initial(
        Guid clientId,
        Guid originalMembershipId,
        IReadOnlyList<MembershipTypeCatalogItem> activeOrdinaryTypes,
        PreviewIssuedMembershipSaleCorrectionResult previewResult,
        ReceptionSearchContext searchContext,
        DateTime occurredAtLocal) => new(
        new IssuedMembershipSaleCorrectionFormInput
        {
            ClientId = clientId,
            OriginalMembershipId = originalMembershipId,
            OccurredAtLocal = occurredAtLocal,
            ExpectedDependencyToken = previewResult.Preview?.DependencyToken,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            SearchQuery = searchContext.Query,
            SearchMode = searchContext.Mode,
            SearchIncludeInactive = searchContext.IncludeInactive,
            SearchPageCursor = searchContext.PageCursor,
        },
        activeOrdinaryTypes,
        previewResult,
        []);

    public static IssuedMembershipSaleCorrectionFormViewModel FromSubmission(
        IssuedMembershipSaleCorrectionFormInput input,
        IReadOnlyList<MembershipTypeCatalogItem> activeOrdinaryTypes,
        PreviewIssuedMembershipSaleCorrectionResult previewResult,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(activeOrdinaryTypes);
        ArgumentNullException.ThrowIfNull(previewResult);
        ArgumentNullException.ThrowIfNull(errors);

        var replacement = previewResult.Preview?.Replacement;
        var isReplace = input.Mode == IssuedMembershipSaleCorrectionMode.Replace;
        return new IssuedMembershipSaleCorrectionFormViewModel(
            new IssuedMembershipSaleCorrectionFormInput
            {
                ClientId = input.ClientId,
                OriginalMembershipId = input.OriginalMembershipId,
                Mode = input.Mode,
                ReplacementMembershipTypeId = isReplace ? replacement?.MembershipTypeId ?? input.ReplacementMembershipTypeId : null,
                ExpectedMembershipTypeUpdatedAt = isReplace ? replacement?.ExpectedMembershipTypeUpdatedAt : null,
                ReplacementStartDate = isReplace ? replacement?.StartDate ?? input.ReplacementStartDate : null,
                Reason = input.Reason,
                Comment = input.Comment,
                OccurredAtLocal = input.OccurredAtLocal,
                ExpectedDependencyToken = previewResult.Preview?.DependencyToken,
                Confirmed = errors.Count == 0 && input.Confirmed,
                IdempotencyKey = errors.Any(error => error.Code == CommandErrorCode.DuplicateSubmission)
                    ? Guid.NewGuid().ToString("N")
                    : input.IdempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            activeOrdinaryTypes,
            previewResult,
            errors);
    }
}

public enum IssuedMembershipSaleCorrectionMode { Cancel, Replace }

public sealed class IssuedMembershipSaleCorrectionFormInput
{
    public Guid ClientId { get; set; }
    public Guid OriginalMembershipId { get; set; }
    public IssuedMembershipSaleCorrectionMode? Mode { get; set; }
    public Guid? ReplacementMembershipTypeId { get; set; }
    public DateTimeOffset? ExpectedMembershipTypeUpdatedAt { get; set; }
    public DateOnly? ReplacementStartDate { get; set; }
    public string? Reason { get; set; }
    public string? Comment { get; set; }
    public DateTime? OccurredAtLocal { get; set; }
    public string? ExpectedDependencyToken { get; set; }
    public bool Confirmed { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SearchQuery { get; set; }
    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;
    public bool SearchIncludeInactive { get; set; }
    public string? SearchPageCursor { get; set; }
}
