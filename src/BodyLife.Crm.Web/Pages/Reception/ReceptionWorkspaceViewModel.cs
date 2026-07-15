using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed record ReceptionWorkspaceViewModel(
    string? Query,
    ClientSearchMode Mode,
    bool IncludeInactive,
    string? PageCursor,
    SearchClientsResult? SearchResult,
    CreateClientFormViewModel? CreateClientForm,
    ClientProfileViewModel Profile)
{
    public static ReceptionWorkspaceViewModel Empty { get; } = new(
        Query: null,
        ClientSearchMode.Auto,
        IncludeInactive: false,
        PageCursor: null,
        SearchResult: null,
        CreateClientForm: null,
        ClientProfileViewModel.Empty);
}

public sealed record ReceptionSearchContext(
    string? Query,
    ClientSearchMode Mode,
    bool IncludeInactive,
    string? PageCursor);

public sealed record ClientProfileViewModel(
    GetClientProfileResult? Result,
    UpdateClientFormViewModel? UpdateClientForm,
    CardAssignmentFormViewModel? CardAssignmentForm,
    MarkVisitFormViewModel? MarkVisitForm,
    IReadOnlyList<CancelVisitFormViewModel> CancelVisitForms,
    string? OperationMessage,
    bool OperationSucceeded)
{
    public static ClientProfileViewModel Empty { get; } = new(
        Result: null,
        UpdateClientForm: null,
        CardAssignmentForm: null,
        MarkVisitForm: null,
        CancelVisitForms: [],
        OperationMessage: null,
        OperationSucceeded: false);

    public static ClientProfileViewModel FromResult(
        GetClientProfileResult? result,
        ReceptionSearchContext searchContext,
        string? operationMessage = null,
        bool operationSucceeded = false,
        UpdateClientFormViewModel? updateClientForm = null,
        CardAssignmentFormViewModel? cardAssignmentForm = null,
        MarkVisitFormViewModel? markVisitForm = null,
        IReadOnlyList<CancelVisitFormViewModel>? cancelVisitForms = null)
    {
        if (updateClientForm is null
            && result?.Profile is { } profile
            && profile.AllowedActions.IsAllowed(ClientProfileActionKeys.UpdateClient))
        {
            updateClientForm = UpdateClientFormViewModel.FromProfile(profile, searchContext);
        }

        if (cardAssignmentForm is null
            && result?.Profile is { } cardProfile
            && cardProfile.AllowedActions.IsAllowed(ClientProfileActionKeys.AssignOrChangeCard))
        {
            cardAssignmentForm = CardAssignmentFormViewModel.FromProfile(
                cardProfile,
                searchContext);
        }

        return new ClientProfileViewModel(
            result,
            updateClientForm,
            cardAssignmentForm,
            markVisitForm,
            cancelVisitForms ?? [],
            operationMessage,
            operationSucceeded);
    }
}
