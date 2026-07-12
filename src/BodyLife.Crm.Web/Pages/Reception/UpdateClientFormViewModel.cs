using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record UpdateClientFormViewModel(
    UpdateClientFormInput Input,
    IReadOnlyList<UpdateClientDuplicateWarningViewModel> DuplicateWarnings,
    IReadOnlyList<CommandError> Errors,
    bool IsOpen)
{
    public static UpdateClientFormViewModel FromProfile(
        ClientProfile profile,
        ReceptionSearchContext searchContext,
        IReadOnlyList<CommandError>? errors = null,
        bool isOpen = false)
    {
        return new UpdateClientFormViewModel(
            new UpdateClientFormInput
            {
                ClientId = profile.ClientId,
                ExpectedUpdatedAt = profile.UpdatedAt,
                Surname = profile.Surname,
                Name = profile.Name,
                Patronymic = profile.Patronymic,
                Phone = profile.Phone,
                Comment = profile.Comment,
                OperationalStatus = profile.OperationalStatus,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                SearchQuery = searchContext.Query,
                SearchMode = searchContext.Mode,
                SearchIncludeInactive = searchContext.IncludeInactive,
                SearchPageCursor = searchContext.PageCursor,
            },
            DuplicateWarnings: [],
            errors ?? [],
            isOpen);
    }

    public static UpdateClientFormViewModel FromSubmission(
        UpdateClientFormInput input,
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

                return new UpdateClientDuplicateWarningViewModel(
                    candidate,
                    acknowledgement?.Acknowledged ?? false,
                    acknowledgement?.Reason);
            })
            .ToArray();

        return new UpdateClientFormViewModel(
            input,
            warnings,
            errors,
            IsOpen: true);
    }
}

public sealed class UpdateClientFormInput
{
    public Guid ClientId { get; set; }

    public DateTimeOffset ExpectedUpdatedAt { get; set; }

    public string? Surname { get; set; }

    public string? Name { get; set; }

    public string? Patronymic { get; set; }

    public string? Phone { get; set; }

    public string? Comment { get; set; }

    public ClientOperationalStatus OperationalStatus { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? SearchQuery { get; set; }

    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;

    public bool SearchIncludeInactive { get; set; }

    public string? SearchPageCursor { get; set; }

    public List<UpdateClientDuplicateAcknowledgementInput>? DuplicateAcknowledgements { get; set; }
}

public sealed class UpdateClientDuplicateAcknowledgementInput
{
    public Guid MatchedClientId { get; set; }

    public ClientDuplicateWarningType WarningType { get; set; }

    public bool Acknowledged { get; set; }

    public string? Reason { get; set; }
}

public sealed record UpdateClientDuplicateWarningViewModel(
    ClientDuplicateCandidate Candidate,
    bool Acknowledged,
    string? Reason);
