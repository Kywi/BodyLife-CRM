using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record CancelFreezeFormViewModel(
    CancelFreezeFormInput Input,
    ClientMembershipSummary Membership,
    MembershipExtensionExplanation Freeze,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int ReasonMaxLength = 1000;
    public const int CommentMaxLength = 1000;

    public static CancelFreezeFormViewModel FromFreeze(
        Guid clientId,
        ClientMembershipSummary membership,
        MembershipExtensionExplanation freeze,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(freeze);
        EnsureCancelable(membership, freeze);

        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        return new CancelFreezeFormViewModel(
            new CancelFreezeFormInput
            {
                ClientId = clientId,
                FreezeId = freeze.SourceId,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            membership,
            freeze,
            Errors: [],
            IsOpen: false);
    }

    public static CancelFreezeFormViewModel FromSubmission(
        CancelFreezeFormInput input,
        ClientMembershipSummary membership,
        MembershipExtensionExplanation freeze,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(freeze);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(input, membership, freeze);

        return new CancelFreezeFormViewModel(
            CopyInput(
                input,
                idempotencyKey: input.IdempotencyKey,
                confirmed: input.Confirmed),
            membership,
            freeze,
            errors,
            IsOpen: true);
    }

    public static CancelFreezeFormViewModel FromCanonicalRefresh(
        CancelFreezeFormInput submittedInput,
        CancelFreezeFormViewModel currentForm,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(currentForm);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(submittedInput, currentForm.Membership, currentForm.Freeze);

        return new CancelFreezeFormViewModel(
            CopyInput(
                submittedInput,
                idempotencyKey: currentForm.Input.IdempotencyKey,
                confirmed: false),
            currentForm.Membership,
            currentForm.Freeze,
            errors,
            IsOpen: true);
    }

    private static CancelFreezeFormInput CopyInput(
        CancelFreezeFormInput input,
        string? idempotencyKey,
        bool confirmed)
    {
        return new CancelFreezeFormInput
        {
            ClientId = input.ClientId,
            FreezeId = input.FreezeId,
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

    private static void EnsureMatches(
        CancelFreezeFormInput input,
        ClientMembershipSummary membership,
        MembershipExtensionExplanation freeze)
    {
        EnsureCancelable(membership, freeze);
        if (input.ClientId == Guid.Empty || input.FreezeId != freeze.SourceId)
        {
            throw new ArgumentException(
                "Submitted cancellation form does not match the canonical Freeze row.",
                nameof(input));
        }
    }

    private static void EnsureCancelable(
        ClientMembershipSummary membership,
        MembershipExtensionExplanation freeze)
    {
        if (freeze.SourceKind != MembershipExtensionSourceKind.Freeze
            || freeze.Status != MembershipExtensionSourceStatus.Active
            || freeze.MembershipId != membership.MembershipId)
        {
            throw new ArgumentException(
                "An active Freeze explanation for the selected Membership is required.",
                nameof(freeze));
        }
    }
}

public sealed class CancelFreezeFormInput
{
    public Guid ClientId { get; set; }

    public Guid FreezeId { get; set; }

    public string? Reason { get; set; }

    public string? Comment { get; set; }

    public bool Confirmed { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
