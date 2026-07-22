using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record CorrectPaymentFormViewModel(
    CorrectPaymentFormInput Input,
    ClientPaymentRow Payment,
    IReadOnlyList<ClientMembershipSummary> MembershipOptions,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int ReasonMaxLength = 1000;
    public const int CommentMaxLength = 1000;
    public const string Currency = "UAH";

    public static IReadOnlyList<PaymentContext> SupportedContexts { get; } = Array.AsReadOnly(
    [
        PaymentContext.MembershipSale,
        PaymentContext.OneOff,
        PaymentContext.Trial,
        PaymentContext.Other,
    ]);

    public static CorrectPaymentFormViewModel FromPayment(
        ClientPaymentRow payment,
        ClientProfile profile,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(profile);
        EnsureCorrectable(payment);
        EnsureClientMatches(payment, profile);

        return new CorrectPaymentFormViewModel(
            new CorrectPaymentFormInput
            {
                ClientId = payment.ClientId,
                OriginalPaymentId = payment.PaymentId,
                Mode = PaymentCorrectionMode.Replace,
                ReplacementAmount = payment.Amount.Amount,
                ReplacementPaymentContext = payment.PaymentContext,
                ReplacementMembershipId = payment.MembershipId,
                ReplacementOccurredAtLocal = BusinessTimeZone.ConvertInstantToLocal(payment.OccurredAt),
                ReplacementComment = payment.Comment,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            payment,
            profile.Membership.Timeline,
            Errors: [],
            IsOpen: false);
    }

    public static CorrectPaymentFormViewModel FromSubmission(
        CorrectPaymentFormInput input,
        ClientPaymentRow payment,
        ClientProfile profile,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(input, payment, profile);

        return new CorrectPaymentFormViewModel(
            NormalizeInput(input, profile, input.IdempotencyKey, input.Confirmed),
            payment,
            profile.Membership.Timeline,
            errors,
            IsOpen: true);
    }

    public static CorrectPaymentFormViewModel FromCanonicalRefresh(
        CorrectPaymentFormInput submittedInput,
        CorrectPaymentFormViewModel currentForm,
        ClientProfile profile,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(submittedInput);
        ArgumentNullException.ThrowIfNull(currentForm);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(errors);
        EnsureMatches(submittedInput, currentForm.Payment, profile);

        return new CorrectPaymentFormViewModel(
            NormalizeInput(
                submittedInput,
                profile,
                currentForm.Input.IdempotencyKey,
                confirmed: false),
            currentForm.Payment,
            profile.Membership.Timeline,
            errors,
            IsOpen: true);
    }

    private static CorrectPaymentFormInput NormalizeInput(
        CorrectPaymentFormInput input,
        ClientProfile profile,
        string? idempotencyKey,
        bool confirmed)
    {
        var mode = input.Mode is { } submittedMode && Enum.IsDefined(submittedMode)
            ? (PaymentCorrectionMode?)submittedMode
            : null;
        var paymentContext = input.ReplacementPaymentContext is { } submittedContext
            && SupportedContexts.Contains(submittedContext)
                ? (PaymentContext?)submittedContext
                : null;
        var membershipId = input.ReplacementMembershipId is { } submittedMembershipId
            && profile.Membership.Timeline.Any(option =>
                option.MembershipId == submittedMembershipId)
                    ? (Guid?)submittedMembershipId
                    : null;

        return new CorrectPaymentFormInput
        {
            ClientId = input.ClientId,
            OriginalPaymentId = input.OriginalPaymentId,
            Mode = mode,
            ReplacementAmount = input.ReplacementAmount,
            ReplacementPaymentContext = paymentContext,
            ReplacementMembershipId = membershipId,
            ReplacementOccurredAtLocal = input.ReplacementOccurredAtLocal,
            ReplacementComment = input.ReplacementComment,
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
        CorrectPaymentFormInput input,
        ClientPaymentRow payment,
        ClientProfile profile)
    {
        EnsureCorrectable(payment);
        EnsureClientMatches(payment, profile);
        if (input.ClientId != payment.ClientId
            || input.OriginalPaymentId != payment.PaymentId)
        {
            throw new ArgumentException(
                "Submitted correction form does not match the canonical Payment row.",
                nameof(input));
        }
    }

    private static void EnsureClientMatches(ClientPaymentRow payment, ClientProfile profile)
    {
        if (payment.ClientId != profile.ClientId)
        {
            throw new ArgumentException(
                "The canonical Payment row does not belong to the profile client.",
                nameof(payment));
        }
    }

    private static void EnsureCorrectable(ClientPaymentRow payment)
    {
        if (payment.Status != ClientPaymentRowStatus.Active
            || payment.PaymentContext == PaymentContext.NegativeClosure
            || !payment.AllowedActions.IsAllowed(PaymentActionKeys.Correct))
        {
            throw new ArgumentException(
                "An active non-negative-closure Payment with server correction permission is required.",
                nameof(payment));
        }
    }
}

public sealed class CorrectPaymentFormInput
{
    public Guid ClientId { get; set; }

    public Guid OriginalPaymentId { get; set; }

    public PaymentCorrectionMode? Mode { get; set; }

    public decimal? ReplacementAmount { get; set; }

    public PaymentContext? ReplacementPaymentContext { get; set; }

    public Guid? ReplacementMembershipId { get; set; }

    public DateTime? ReplacementOccurredAtLocal { get; set; }

    public string? ReplacementComment { get; set; }

    public string? Reason { get; set; }

    public string? Comment { get; set; }

    public bool Confirmed { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
