using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record CreateClientFormViewModel(
    CreateClientFormInput Input,
    IReadOnlyList<CreateClientDuplicateWarningViewModel> DuplicateWarnings,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public static CreateClientFormViewModel FromSearchContext(
        ReceptionSearchContext searchContext)
    {
        return new CreateClientFormViewModel(
            new CreateClientFormInput
            {
                CardNumber = searchContext.Mode == ClientSearchMode.Card
                    ? searchContext.Query
                    : null,
                OperationalStatus = ClientOperationalStatus.Active,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            DuplicateWarnings: [],
            Errors: [],
            IsOpen: true);
    }

    public static CreateClientFormViewModel FromSubmission(
        CreateClientFormInput input,
        IReadOnlyList<ClientDuplicateCandidate> candidates,
        IReadOnlyList<CommandError> errors)
    {
        var postedAcknowledgements = (input.DuplicateAcknowledgements ?? [])
            .GroupBy(acknowledgement => new
            {
                acknowledgement.MatchedClientId,
                acknowledgement.WarningType,
            })
            .ToDictionary(group => group.Key, group => group.First());
        var warnings = candidates
            .Select(candidate =>
            {
                postedAcknowledgements.TryGetValue(
                    new
                    {
                        candidate.MatchedClientId,
                        candidate.WarningType,
                    },
                    out var acknowledgement);

                return new CreateClientDuplicateWarningViewModel(
                    candidate,
                    acknowledgement?.Acknowledged ?? false,
                    acknowledgement?.Reason);
            })
            .ToArray();

        return new CreateClientFormViewModel(
            input,
            warnings,
            errors,
            IsOpen: true);
    }
}

public sealed class CreateClientFormInput
{
    public string? Surname { get; set; }

    public string? Name { get; set; }

    public string? Patronymic { get; set; }

    public string? Phone { get; set; }

    public string? CardNumber { get; set; }

    public string? Comment { get; set; }

    public ClientOperationalStatus OperationalStatus { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }

    public List<CreateClientDuplicateAcknowledgementInput>? DuplicateAcknowledgements { get; set; }
}

public sealed class CreateClientDuplicateAcknowledgementInput
{
    public Guid MatchedClientId { get; set; }

    public ClientDuplicateWarningType WarningType { get; set; }

    public bool Acknowledged { get; set; }

    public string? Reason { get; set; }
}

public sealed record CreateClientDuplicateWarningViewModel(
    ClientDuplicateCandidate Candidate,
    bool Acknowledged,
    string? Reason);
