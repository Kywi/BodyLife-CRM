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

        var selectedType = membershipTypesResult.Items.FirstOrDefault();
        return new IssueMembershipFormViewModel(
            new IssueMembershipFormInput
            {
                ClientId = clientId,
                MembershipTypeId = selectedType?.MembershipTypeId,
                StartDate = startDate,
                PaymentAmount = selectedType?.Price.Amount,
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
                StartDate = input.StartDate,
                NegativeHandlingDecision = negativeHandlingDecision,
                IncludePayment = input.IncludePayment,
                PaymentAmount = input.PaymentAmount ?? selectedType?.Price.Amount,
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

    public static string DisplayError(CommandError error)
    {
        return error.Code switch
        {
            CommandErrorCode.DuplicateSubmission =>
                "This issue form was already used with different data. Review and retry with the refreshed form.",
            CommandErrorCode.PermissionDenied =>
                "The current account or session is not allowed to issue this membership.",
            CommandErrorCode.NotFound when error.Field == "membershipTypeId" =>
                "The selected membership type is no longer available. Review the current catalog.",
            CommandErrorCode.NotFound =>
                "The client is no longer available. Refresh the reception workspace.",
            CommandErrorCode.MembershipTypeInactive =>
                "The selected membership type became inactive. Choose an active type.",
            CommandErrorCode.NegativeDecisionRequired =>
                "Choose how the existing negative visits remain handled before issuing.",
            CommandErrorCode.MembershipNotEligible =>
                "The selected negative handling option is not available. Review the refreshed preview.",
            CommandErrorCode.RecalculationFailed =>
                "Canonical membership state is unavailable. Refresh before issuing.",
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                "Membership data changed while submitting. Canonical profile and preview data were refreshed.",
            _ => error.Message,
        };
    }
}

public sealed class IssueMembershipFormInput
{
    public Guid ClientId { get; set; }

    public Guid? MembershipTypeId { get; set; }

    public DateOnly? StartDate { get; set; }

    public MembershipNegativeHandlingDecision? NegativeHandlingDecision { get; set; }

    public bool IncludePayment { get; set; }

    public decimal? PaymentAmount { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
