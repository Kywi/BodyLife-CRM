using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record IssueMembershipFormViewModel(
    IssueMembershipFormInput Input,
    GetMembershipTypesForIssueResult MembershipTypesResult,
    PreviewIssueMembershipResult? PreviewResult,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int CommentMaxLength = 1000;
    public const string Currency = "UAH";

    public IReadOnlyList<MembershipTypeCatalogItem> MembershipTypes =>
        MembershipTypesResult.Items;

    public MembershipIssuePreview? Preview => PreviewResult?.Preview;

    public int? MaxNegativeCoverageCount => Preview?.ExistingNegativeState is { } negativeState
        ? Math.Min(negativeState.OpenConcreteVisitCount, Preview.Snapshot.VisitsLimit)
        : null;

    public IReadOnlyList<MembershipNegativeVisitCoverageCandidate> CoveredNegativeVisits =>
        Preview?.ExistingNegativeState?.OpenConcreteVisits
            .Take(Preview.CoveredNegativeVisitCount)
            .ToArray()
        ?? [];

    public bool CanSubmit => PreviewResult is
    {
        Status: PreviewIssueMembershipStatus.Success,
        Preview: { CanProceedToIssue: true },
    } && PreviewResult.AllowedActions.IsAllowed(MembershipActionKeys.Issue);

    public static IssueMembershipFormViewModel FromInitialQueries(
        Guid clientId,
        DateOnly startDate,
        GetMembershipTypesForIssueResult membershipTypesResult,
        PreviewIssueMembershipResult? previewResult,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(membershipTypesResult);

        return new IssueMembershipFormViewModel(
            new IssueMembershipFormInput
            {
                ClientId = clientId,
                StartDate = startDate,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            membershipTypesResult,
            previewResult,
            Errors: [],
            IsOpen: false);
    }

    public static IssueMembershipFormViewModel FromSubmission(
        IssueMembershipFormInput input,
        GetMembershipTypesForIssueResult membershipTypesResult,
        PreviewIssueMembershipResult? previewResult,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(membershipTypesResult);
        ArgumentNullException.ThrowIfNull(errors);

        var selectedType = input.MembershipTypeId is { } submittedTypeId
            ? membershipTypesResult.Items.SingleOrDefault(item =>
                item.MembershipTypeId == submittedTypeId)
            : null;
        var preview = previewResult?.Preview;
        var negativeHandlingDecision = input.NegativeHandlingDecision is { } decision
            && preview?.NegativeHandlingOptions.Any(option =>
                option.Decision == decision
                && option.IsAvailable) == true
                    ? (MembershipNegativeHandlingDecision?)decision
                    : null;
        var idempotencyKey = errors.Any(error =>
            error.Code == CommandErrorCode.DuplicateSubmission)
                ? Guid.NewGuid().ToString("N")
                : input.IdempotencyKey;

        return new IssueMembershipFormViewModel(
            new IssueMembershipFormInput
            {
                ClientId = input.ClientId,
                MembershipTypeId = selectedType?.MembershipTypeId,
                ExpectedMembershipTypeUpdatedAt = preview?.MembershipTypeUpdatedAt,
                StartDate = input.StartDate,
                NegativeHandlingDecision = negativeHandlingDecision,
                NegativeCoverageCount = negativeHandlingDecision
                    == MembershipNegativeHandlingDecision.CoverWithNewMembership
                    ? input.NegativeCoverageCount
                    : null,
                ExpectedOldestOpenNegativeVisitId = negativeHandlingDecision
                    == MembershipNegativeHandlingDecision.CoverWithNewMembership
                    ? preview?.ExpectedOldestOpenNegativeVisitId
                    : null,
                Comment = input.Comment,
                IdempotencyKey = idempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            membershipTypesResult,
            previewResult,
            errors,
            IsOpen: true);
    }

}

public sealed class IssueMembershipFormInput
{
    public Guid ClientId { get; set; }

    public Guid? MembershipTypeId { get; set; }

    public DateTimeOffset? ExpectedMembershipTypeUpdatedAt { get; set; }

    public DateOnly? StartDate { get; set; }

    public MembershipNegativeHandlingDecision? NegativeHandlingDecision { get; set; }

    public int? NegativeCoverageCount { get; set; }

    public Guid? ExpectedOldestOpenNegativeVisitId { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
