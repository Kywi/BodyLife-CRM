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
    IssueMembershipFormViewModel? IssueMembershipForm,
    AddPaymentFormViewModel? AddPaymentForm,
    AddFreezeFormViewModel? AddFreezeForm,
    IReadOnlyList<CancelFreezeFormViewModel> CancelFreezeForms,
    IReadOnlyList<CancelVisitFormViewModel> CancelVisitForms,
    IReadOnlyList<CorrectPaymentFormViewModel> CorrectPaymentForms,
    string? OperationMessage,
    bool OperationSucceeded)
{
    public static ClientProfileViewModel Empty { get; } = new(
        Result: null,
        UpdateClientForm: null,
        CardAssignmentForm: null,
        MarkVisitForm: null,
        IssueMembershipForm: null,
        AddPaymentForm: null,
        AddFreezeForm: null,
        CancelFreezeForms: [],
        CancelVisitForms: [],
        CorrectPaymentForms: [],
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
        IssueMembershipFormViewModel? issueMembershipForm = null,
        AddPaymentFormViewModel? addPaymentForm = null,
        AddFreezeFormViewModel? addFreezeForm = null,
        IReadOnlyList<CancelFreezeFormViewModel>? cancelFreezeForms = null,
        IReadOnlyList<CancelVisitFormViewModel>? cancelVisitForms = null,
        IReadOnlyList<CorrectPaymentFormViewModel>? correctPaymentForms = null)
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
            issueMembershipForm,
            addPaymentForm,
            addFreezeForm,
            cancelFreezeForms ?? [],
            cancelVisitForms ?? [],
            correctPaymentForms ?? [],
            operationMessage,
            operationSucceeded);
    }
}
