using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record MarkVisitFormViewModel(
    MarkVisitFormInput Input,
    GetMarkVisitOptionsResult OptionsResult,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public MarkVisitOptions? Options => OptionsResult.Options;

    public bool IsAvailable => OptionsResult is
    {
        Status: GetMarkVisitOptionsStatus.Success,
        Options: not null,
    } && OptionsResult.AllowedActions.IsAllowed(VisitActionKeys.Mark);

    public static MarkVisitFormViewModel FromQuery(
        Guid clientId,
        DateTimeOffset occurredAt,
        GetMarkVisitOptionsResult optionsResult,
        ReceptionSearchContext searchContext)
    {
        ArgumentNullException.ThrowIfNull(optionsResult);

        var options = optionsResult.Options;
        var suggestedMembershipId = options?.SuggestedMembershipId;
        var input = new MarkVisitFormInput
        {
            ClientId = clientId,
            VisitKind = suggestedMembershipId.HasValue
                ? VisitKind.Membership
                : null,
            MembershipId = suggestedMembershipId,
            OccurredAt = options?.OccurredAt ?? occurredAt,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            SearchQuery = searchContext.Query,
            SearchMode = searchContext.Mode,
            SearchIncludeInactive = searchContext.IncludeInactive,
            SearchPageCursor = searchContext.PageCursor,
        };

        return new MarkVisitFormViewModel(
            input,
            optionsResult,
            Errors: [],
            IsOpen: false);
    }

    public static MarkVisitFormViewModel FromSubmission(
        MarkVisitFormInput input,
        GetMarkVisitOptionsResult optionsResult,
        IReadOnlyList<CommandError> errors)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(optionsResult);
        ArgumentNullException.ThrowIfNull(errors);

        var submittedAcknowledgements = (input.Acknowledgements ?? [])
            .Distinct()
            .OrderBy(acknowledgement => acknowledgement)
            .ToArray();
        var currentAcknowledgements = optionsResult.Options?
            .MembershipOptions
            .SingleOrDefault(option => option.MembershipId == input.MembershipId)?
            .RequiredAcknowledgements
            .OrderBy(acknowledgement => acknowledgement)
            .ToArray() ?? [];
        var retainedAcknowledgements = submittedAcknowledgements.SequenceEqual(
            currentAcknowledgements)
                ? submittedAcknowledgements
                : [];

        return new MarkVisitFormViewModel(
            new MarkVisitFormInput
            {
                ClientId = input.ClientId,
                VisitKind = input.VisitKind,
                MembershipId = input.MembershipId,
                Acknowledgements = retainedAcknowledgements.ToList(),
                OccurredAt = optionsResult.Options?.OccurredAt ?? input.OccurredAt,
                Comment = input.Comment,
                IdempotencyKey = input.IdempotencyKey,
                SearchQuery = input.SearchQuery,
                SearchMode = input.SearchMode,
                SearchIncludeInactive = input.SearchIncludeInactive,
                SearchPageCursor = input.SearchPageCursor,
            },
            optionsResult,
            errors,
            IsOpen: true);
    }
}

public sealed class MarkVisitFormInput
{
    public Guid ClientId { get; set; }

    public VisitKind? VisitKind { get; set; }

    public Guid? MembershipId { get; set; }

    public List<MembershipVisitAcknowledgement>? Acknowledgements { get; set; } = [];

    public DateTimeOffset OccurredAt { get; set; }

    public string? Comment { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }
}
