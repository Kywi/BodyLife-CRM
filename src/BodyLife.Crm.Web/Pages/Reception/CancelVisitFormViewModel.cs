using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record CancelVisitFormViewModel(
    CancelVisitFormInput Input,
    ClientVisitRow Visit,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int ReasonMaxLength = 1000;
    public const int CommentMaxLength = 1000;

    public static CancelVisitFormViewModel FromVisit(
        ClientVisitRow visit,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(visit);
        EnsureCancelable(visit);

        return new CancelVisitFormViewModel(
            new CancelVisitFormInput
            {
                ClientId = visit.ClientId,
                VisitId = visit.VisitId,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            visit,
            Errors: [],
            IsOpen: false);
    }

    public static CancelVisitFormViewModel FromSubmission(
        CancelVisitFormInput input,
        ClientVisitRow visit,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(input, visit);

        return new CancelVisitFormViewModel(
            CopyInput(
                input,
                idempotencyKey: input.IdempotencyKey,
                confirmed: input.Confirmed),
            visit,
            errors,
            IsOpen: true);
    }

    public static CancelVisitFormViewModel FromCanonicalRefresh(
        CancelVisitFormInput submittedInput,
        CancelVisitFormViewModel currentForm,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(currentForm);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(submittedInput, currentForm.Visit);

        return new CancelVisitFormViewModel(
            CopyInput(
                submittedInput,
                idempotencyKey: currentForm.Input.IdempotencyKey,
                confirmed: false),
            currentForm.Visit,
            errors,
            IsOpen: true);
    }

    public static string DisplayError(CommandError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Code switch
        {
            CommandErrorCode.ReasonRequired =>
                "Enter why this Visit should be canceled.",
            CommandErrorCode.AlreadyCanceled =>
                "This Visit was already canceled. Canonical history was refreshed.",
            CommandErrorCode.DayClosedRequiresOwner =>
                "This Visit belongs to a reconciled day and requires Owner correction.",
            CommandErrorCode.PermissionDenied =>
                "The current account or session cannot cancel this Visit.",
            CommandErrorCode.NotFound =>
                "This Visit is no longer available. Canonical history was refreshed.",
            CommandErrorCode.DuplicateSubmission =>
                "This cancellation form was already used with different data. Review the refreshed form before retrying.",
            CommandErrorCode.ConcurrencyConflict or CommandErrorCode.StaleState =>
                "Visit state changed. Canonical history was refreshed; review before retrying.",
            CommandErrorCode.RecalculationFailed =>
                "The Visit was not canceled because canonical Membership state could not be recalculated.",
            _ => error.Message,
        };
    }

    private static CancelVisitFormInput CopyInput(
        CancelVisitFormInput input,
        string? idempotencyKey,
        bool confirmed)
    {
        return new CancelVisitFormInput
        {
            ClientId = input.ClientId,
            VisitId = input.VisitId,
            Reason = input.Reason,
            Comment = input.Comment,
            Confirmed = confirmed,
            IdempotencyKey = idempotencyKey,
            SearchQuery = input.SearchQuery,
            SearchMode = input.SearchMode,
            SearchIncludeInactive = input.SearchIncludeInactive,
            SearchPageCursor = input.SearchPageCursor,
        };
    }

    private static void EnsureMatches(CancelVisitFormInput input, ClientVisitRow visit)
    {
        EnsureCancelable(visit);
        if (input.ClientId != visit.ClientId || input.VisitId != visit.VisitId)
        {
            throw new ArgumentException(
                "Submitted cancellation form does not match the canonical Visit row.",
                nameof(input));
        }
    }

    private static void EnsureCancelable(ClientVisitRow visit)
    {
        if (visit.Status != ClientVisitRowStatus.Active
            || !visit.AllowedActions.IsAllowed(VisitActionKeys.Cancel))
        {
            throw new ArgumentException(
                "An active Visit with server cancellation permission is required.",
                nameof(visit));
        }
    }
}

public sealed class CancelVisitFormInput
{
    public Guid ClientId { get; set; }

    public Guid VisitId { get; set; }

    public string? Reason { get; set; }

    public string? Comment { get; set; }

    public bool Confirmed { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
