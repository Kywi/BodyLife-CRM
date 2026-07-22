using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Payments;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record AddPaymentFormViewModel(
    AddPaymentFormInput Input,
    IReadOnlyList<ClientMembershipSummary> MembershipOptions,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public const int CommentMaxLength = 1000;
    public const string Currency = "UAH";

    public static IReadOnlyList<PaymentContext> SupportedContexts { get; } = Array.AsReadOnly(
    [
        PaymentContext.MembershipSale,
        PaymentContext.OneOff,
        PaymentContext.Trial,
        PaymentContext.Other,
    ]);

    public static AddPaymentFormViewModel FromProfile(
        ClientProfile profile,
        DateTimeOffset occurredAt,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new AddPaymentFormViewModel(
            new AddPaymentFormInput
            {
                ClientId = profile.ClientId,
                OccurredAtUtc = occurredAt.UtcDateTime,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            profile.Membership.Timeline,
            Errors: [],
            IsOpen: false);
    }

    public static AddPaymentFormViewModel FromSubmission(
        AddPaymentFormInput input,
        ClientProfile profile,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(errors);

        var membershipId = input.MembershipId is { } submittedMembershipId
            && profile.Membership.Timeline.Any(option =>
                option.MembershipId == submittedMembershipId)
                    ? (Guid?)submittedMembershipId
                    : null;
        var paymentContext = input.PaymentContext is { } submittedContext
            && SupportedContexts.Contains(submittedContext)
                ? (PaymentContext?)submittedContext
                : null;
        var idempotencyKey = errors.Any(error =>
            error.Code == CommandErrorCode.DuplicateSubmission)
                ? Guid.NewGuid().ToString("N")
                : input.IdempotencyKey;

        return new AddPaymentFormViewModel(
            new AddPaymentFormInput
            {
                ClientId = profile.ClientId,
                Amount = input.Amount,
                PaymentContext = paymentContext,
                MembershipId = membershipId,
                OccurredAtUtc = input.OccurredAtUtc,
                Comment = input.Comment,
                IdempotencyKey = idempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            profile.Membership.Timeline,
            errors,
            IsOpen: true);
    }

}

public sealed class AddPaymentFormInput
{
    public Guid ClientId { get; set; }

    public decimal? Amount { get; set; }

    public PaymentContext? PaymentContext { get; set; }

    public Guid? MembershipId { get; set; }

    public DateTime? OccurredAtUtc { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
