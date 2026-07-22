using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record AddFreezeFormViewModel(
    AddFreezeFormInput Input,
    IReadOnlyList<ClientMembershipSummary> MembershipOptions,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int ReasonMaxLength = 1000;
    public const int CommentMaxLength = 1000;

    public static AddFreezeFormViewModel FromProfile(
        ClientProfile profile,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var membershipOptions = EligibleMembershipOptions(profile);
        var selectedMembershipId = profile.Membership.CurrentMembership is { } current
            && membershipOptions.Any(option => option.MembershipId == current.MembershipId)
                ? (Guid?)current.MembershipId
                : null;

        return new AddFreezeFormViewModel(
            new AddFreezeFormInput
            {
                ClientId = profile.ClientId,
                MembershipId = selectedMembershipId,
                StartDate = profile.MembershipAsOfDate,
                EndDate = profile.MembershipAsOfDate,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            membershipOptions,
            Errors: [],
            IsOpen: false);
    }

    public static AddFreezeFormViewModel FromSubmission(
        AddFreezeFormInput input,
        ClientProfile profile,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(errors);

        var membershipOptions = EligibleMembershipOptions(profile);
        var membershipId = input.MembershipId is { } submittedMembershipId
            && membershipOptions.Any(option => option.MembershipId == submittedMembershipId)
                ? (Guid?)submittedMembershipId
                : null;
        var idempotencyKey = errors.Any(error =>
            error.Code == CommandErrorCode.DuplicateSubmission)
                ? Guid.NewGuid().ToString("N")
                : input.IdempotencyKey;

        return new AddFreezeFormViewModel(
            new AddFreezeFormInput
            {
                ClientId = profile.ClientId,
                MembershipId = membershipId,
                StartDate = input.StartDate,
                EndDate = input.EndDate,
                Reason = input.Reason,
                Comment = input.Comment,
                IdempotencyKey = idempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            membershipOptions,
            errors,
            IsOpen: true);
    }

    private static IReadOnlyList<ClientMembershipSummary> EligibleMembershipOptions(
        ClientProfile profile)
    {
        return Array.AsReadOnly(
            profile.Membership.Timeline
                .Where(membership => membership.Status is
                    ClientMembershipSummaryStatusCodes.Active
                    or ClientMembershipSummaryStatusCodes.Expired)
                .ToArray());
    }
}

public sealed class AddFreezeFormInput
{
    public Guid ClientId { get; set; }

    public Guid? MembershipId { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? Reason { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
