using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record CardAssignmentFormViewModel(
    CardAssignmentFormInput Input,
    string? CurrentCardNumber,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public bool HasCurrentCard => Input.ExpectedCurrentCardAssignmentId.HasValue;

    public static CardAssignmentFormViewModel FromProfile(
        ClientProfile profile,
        ReceptionSearchContext searchContext,
        IReadOnlyList<CommandError>? errors = null,
        bool isOpen = false)
    {
        return new CardAssignmentFormViewModel(
            new CardAssignmentFormInput
            {
                ClientId = profile.ClientId,
                ExpectedCurrentCardAssignmentId = profile.CurrentCard?.AssignmentId,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            profile.CurrentCard?.CardNumber,
            errors ?? [],
            isOpen);
    }

    public static CardAssignmentFormViewModel FromSubmission(
        CardAssignmentFormInput input,
        ClientProfile canonicalProfile,
        IReadOnlyList<CommandError> errors)
    {
        return new CardAssignmentFormViewModel(
            new CardAssignmentFormInput
            {
                ClientId = input.ClientId,
                ExpectedCurrentCardAssignmentId = input.ExpectedCurrentCardAssignmentId,
                NewCardNumber = input.NewCardNumber,
                ClearCurrentCard = input.ClearCurrentCard,
                Reason = input.Reason,
                IdempotencyKey = input.IdempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            canonicalProfile.CurrentCard?.CardNumber,
            errors,
            IsOpen: true);
    }
}

public sealed class CardAssignmentFormInput
{
    public Guid ClientId { get; set; }

    public Guid? ExpectedCurrentCardAssignmentId { get; set; }

    public string? NewCardNumber { get; set; }

    public bool ClearCurrentCard { get; set; }

    public string? Reason { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
