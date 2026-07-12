using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed class IndexModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<SearchClientsQuery, SearchClientsResult> searchClients,
    IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult> getClientProfile,
    IBodyLifeQueryHandler<
        FindClientDuplicateCandidatesQuery,
        IReadOnlyList<ClientDuplicateCandidate>> findDuplicateCandidates,
    IBodyLifeCommandHandler<CreateClientCommand> createClient,
    IBodyLifeCommandHandler<UpdateClientCommand> updateClient,
    IBodyLifeCommandHandler<AssignOrChangeCardCommand> assignOrChangeCard)
    : PageModel
{
    private const int SearchPageSize = 20;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true, Name = "mode")]
    public ClientSearchMode Mode { get; set; } = ClientSearchMode.Auto;

    [BindProperty(SupportsGet = true, Name = "includeInactive")]
    public bool IncludeInactive { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageCursor")]
    public string? PageCursor { get; set; }

    [BindProperty(SupportsGet = true, Name = "clientId")]
    public Guid? ClientId { get; set; }

    [TempData]
    public string? ClientOperationMessage { get; set; }

    [TempData]
    public string? ClientOperationTone { get; set; }

    public ReceptionWorkspaceViewModel Workspace { get; private set; }
        = ReceptionWorkspaceViewModel.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Workspace = await BuildWorkspaceAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetSearchAsync(CancellationToken cancellationToken)
    {
        if (!IsHtmxRequest())
        {
            return RedirectToCanonicalPage(clientId: null);
        }

        ClientId = null;
        Workspace = await BuildWorkspaceAsync(cancellationToken);
        var selectedClientId = Workspace.Profile.Result?.Profile?.ClientId;
        SetHtmxPushUrl(selectedClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    public async Task<IActionResult> OnGetProfileAsync(CancellationToken cancellationToken)
    {
        if (!IsHtmxRequest())
        {
            return RedirectToCanonicalPage(ClientId);
        }

        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(actor, ClientId ?? Guid.Empty),
            cancellationToken);
        SetHtmxPushUrl(result.Profile?.ClientId ?? ClientId);

        return Partial(
            "_ClientProfile",
            ClientProfileViewModel.FromResult(result, CurrentSearchContext()));
    }

    public async Task<IActionResult> OnPostCreateClientAsync(
        CreateClientFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var acknowledgements = (form.DuplicateAcknowledgements ?? [])
            .Where(acknowledgement => acknowledgement.Acknowledged)
            .Select(acknowledgement => new ClientDuplicateWarningAcknowledgement(
                acknowledgement.MatchedClientId,
                acknowledgement.WarningType,
                acknowledgement.Reason ?? string.Empty))
            .ToArray();
        var command = new CreateClientCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey),
            form.Surname ?? string.Empty,
            form.Name ?? string.Empty,
            form.Patronymic,
            form.Phone,
            form.CardNumber,
            form.Comment,
            form.OperationalStatus,
            acknowledgements);
        var result = await createClient.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCreateAsync(form, result, cancellationToken);
        }

        if (result.Errors.Any(error => error.Code == CommandErrorCode.PermissionDenied))
        {
            return await RenderCanonicalCreatePermissionAsync(form, cancellationToken);
        }

        var createForm = await BuildCreateErrorFormAsync(
            form,
            result.Errors,
            cancellationToken);

        if (IsHtmxRequest())
        {
            return Partial("_CreateClientForm", createForm);
        }

        ApplySearchContext(form);
        ClientId = null;
        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with { CreateClientForm = createForm };

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateClientAsync(
        UpdateClientFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var acknowledgements = (form.DuplicateAcknowledgements ?? [])
            .Where(acknowledgement => acknowledgement.Acknowledged)
            .Select(acknowledgement => new ClientDuplicateWarningAcknowledgement(
                acknowledgement.MatchedClientId,
                acknowledgement.WarningType,
                acknowledgement.Reason ?? string.Empty))
            .ToArray();
        var command = new UpdateClientCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey),
            form.ClientId,
            form.ExpectedUpdatedAt,
            form.Surname ?? string.Empty,
            form.Name ?? string.Empty,
            form.Patronymic,
            form.Phone,
            form.Comment,
            form.OperationalStatus,
            acknowledgements);
        var result = await updateClient.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulUpdateAsync(form, result, cancellationToken);
        }

        var updateForm = await BuildErrorFormAsync(form, result.Errors, cancellationToken);

        if (result.Errors.Any(error => error.Code is
                CommandErrorCode.StaleState or CommandErrorCode.ConcurrencyConflict))
        {
            return await RenderCanonicalConflictAsync(form, result.Errors, cancellationToken);
        }

        if (IsHtmxRequest())
        {
            return Partial("_UpdateClientForm", updateForm);
        }

        ApplySearchContext(form);
        ClientId = form.ClientId;
        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with
        {
            Profile = Workspace.Profile with { UpdateClientForm = updateForm },
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAssignOrChangeCardAsync(
        CardAssignmentFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var command = new AssignOrChangeCardCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason),
            form.ClientId,
            form.ExpectedCurrentCardAssignmentId,
            form.NewCardNumber,
            form.ClearCurrentCard);
        var result = await assignOrChangeCard.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCardAssignmentAsync(form, result, cancellationToken);
        }

        if (RequiresCanonicalCardRefresh(result.Errors))
        {
            return await RenderCanonicalCardConflictAsync(
                form,
                result.Errors,
                cancellationToken);
        }

        var cardForm = await BuildCardErrorFormAsync(form, result.Errors, cancellationToken);

        if (cardForm is null)
        {
            return await RenderCanonicalCardConflictAsync(
                form,
                result.Errors,
                cancellationToken);
        }

        if (IsHtmxRequest())
        {
            return Partial("_CardAssignmentForm", cardForm);
        }

        ApplySearchContext(form);
        ClientId = form.ClientId;
        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with
        {
            Profile = Workspace.Profile with { CardAssignmentForm = cardForm },
        };

        return Page();
    }

    private async Task<IActionResult> RenderSuccessfulCreateAsync(
        CreateClientFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } primaryEntity
            || result.RereadTargetId is not { } rereadTarget
            || primaryEntity.Value != rereadTarget.Value)
        {
            throw new InvalidOperationException(
                "CreateClient did not return the expected canonical client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var message = result.AuditEntryId is { } auditEntryId
            ? $"Client created. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : "Client created.";

        if (!IsHtmxRequest())
        {
            ClientOperationMessage = message;
            ClientOperationTone = "success";
            return RedirectToCanonicalPage(ClientId);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with
        {
            Profile = Workspace.Profile with
            {
                OperationMessage = message,
                OperationSucceeded = true,
            },
        };
        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(ClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<IActionResult> RenderSuccessfulUpdateAsync(
        UpdateClientFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Value != form.ClientId)
        {
            throw new InvalidOperationException(
                "UpdateClient did not return the expected canonical client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var message = result.AuditEntryId is { } auditEntryId
            ? $"Client updated. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : "Client updated.";

        if (!IsHtmxRequest())
        {
            ClientOperationMessage = message;
            ClientOperationTone = "success";
            return RedirectToCanonicalPage(ClientId);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with
        {
            Profile = Workspace.Profile with
            {
                OperationMessage = message,
                OperationSucceeded = true,
            },
        };
        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(ClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<IActionResult> RenderCanonicalCreatePermissionAsync(
        CreateClientFormInput form,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = null;
        Workspace = await BuildWorkspaceAsync(cancellationToken);

        if (!IsHtmxRequest())
        {
            return Page();
        }

        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(clientId: null);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<IActionResult> RenderSuccessfulCardAssignmentAsync(
        CardAssignmentFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Value != form.ClientId)
        {
            throw new InvalidOperationException(
                "AssignOrChangeCard did not return the expected canonical client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var outcome = form.ClearCurrentCard
            ? "Card cleared"
            : form.ExpectedCurrentCardAssignmentId.HasValue
                ? "Card changed"
                : "Card assigned";
        var message = result.AuditEntryId is { } auditEntryId
            ? $"{outcome}. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : $"{outcome}.";

        if (!IsHtmxRequest())
        {
            ClientOperationMessage = message;
            ClientOperationTone = "success";
            return RedirectToCanonicalPage(ClientId);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        Workspace = Workspace with
        {
            Profile = Workspace.Profile with
            {
                OperationMessage = message,
                OperationSucceeded = true,
            },
        };
        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(ClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<IActionResult> RenderCanonicalConflictAsync(
        UpdateClientFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        Workspace = await BuildWorkspaceAsync(cancellationToken);

        if (Workspace.Profile.Result?.Profile is { } profile)
        {
            var freshForm = UpdateClientFormViewModel.FromProfile(
                profile,
                CurrentSearchContext(),
                errors,
                isOpen: true);
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with { UpdateClientForm = freshForm },
            };
        }

        if (!IsHtmxRequest())
        {
            return Page();
        }

        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(ClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<IActionResult> RenderCanonicalCardConflictAsync(
        CardAssignmentFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        Workspace = await BuildWorkspaceAsync(cancellationToken);

        if (Workspace.Profile.Result?.Profile is { } profile
            && profile.AllowedActions.IsAllowed(ClientProfileActionKeys.AssignOrChangeCard))
        {
            var freshForm = CardAssignmentFormViewModel.FromProfile(
                profile,
                CurrentSearchContext(),
                errors,
                isOpen: true);
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with { CardAssignmentForm = freshForm },
            };
        }

        if (!IsHtmxRequest())
        {
            return Page();
        }

        Response.Headers["HX-Retarget"] = "#reception-workspace";
        Response.Headers["HX-Reswap"] = "outerHTML";
        SetHtmxPushUrl(ClientId);

        return Partial("_ReceptionWorkspace", Workspace);
    }

    private async Task<UpdateClientFormViewModel> BuildErrorFormAsync(
        UpdateClientFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientDuplicateCandidate> candidates = [];

        if (errors.Any(error =>
                error.Code == CommandErrorCode.DuplicateWarningNotAcknowledged
                || error.Field?.StartsWith(
                    "duplicateWarningAcknowledgements",
                    StringComparison.Ordinal) == true))
        {
            candidates = await findDuplicateCandidates.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    form.Surname ?? string.Empty,
                    form.Name ?? string.Empty,
                    form.Patronymic,
                    form.Phone,
                    ExcludedClientId: form.ClientId),
                cancellationToken);
        }

        return UpdateClientFormViewModel.FromSubmission(form, candidates, errors);
    }

    private async Task<CreateClientFormViewModel> BuildCreateErrorFormAsync(
        CreateClientFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ClientDuplicateCandidate> candidates = [];

        if (errors.Any(error =>
                error.Code == CommandErrorCode.DuplicateWarningNotAcknowledged
                || error.Field?.StartsWith(
                    "duplicateWarningAcknowledgements",
                    StringComparison.Ordinal) == true))
        {
            candidates = await findDuplicateCandidates.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    form.Surname ?? string.Empty,
                    form.Name ?? string.Empty,
                    form.Patronymic,
                    form.Phone),
                cancellationToken);
        }

        return CreateClientFormViewModel.FromSubmission(form, candidates, errors);
    }

    private async Task<CardAssignmentFormViewModel?> BuildCardErrorFormAsync(
        CardAssignmentFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(actor, form.ClientId),
            cancellationToken);

        return result is
        {
            Status: GetClientProfileStatus.Success,
            Profile: { } profile,
        }
            && profile.AllowedActions.IsAllowed(ClientProfileActionKeys.AssignOrChangeCard)
                ? CardAssignmentFormViewModel.FromSubmission(form, profile, errors)
                : null;
    }

    private static bool RequiresCanonicalCardRefresh(IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is
            CommandErrorCode.StaleState
            or CommandErrorCode.ConcurrencyConflict
            or CommandErrorCode.NotFound
            or CommandErrorCode.PermissionDenied);
    }

    private async Task<ReceptionWorkspaceViewModel> BuildWorkspaceAsync(
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        SearchClientsResult? searchResult = null;

        if (!string.IsNullOrWhiteSpace(Query))
        {
            searchResult = await searchClients.ExecuteAsync(
                new SearchClientsQuery(
                    actor,
                    Query,
                    Mode,
                    IncludeInactive,
                    SearchPageSize,
                    PageCursor),
                cancellationToken);
        }

        var profileClientId = ClientId;

        if (!profileClientId.HasValue
            && searchResult is { Status: SearchClientsStatus.Success, AutoOpenClientId: not null })
        {
            profileClientId = searchResult.AutoOpenClientId;
        }

        GetClientProfileResult? profileResult = null;

        if (profileClientId.HasValue)
        {
            profileResult = await getClientProfile.ExecuteAsync(
                new GetClientProfileQuery(actor, profileClientId.Value),
                cancellationToken);
        }

        var operationMessage = ClientOperationMessage;
        var operationSucceeded = string.Equals(
            ClientOperationTone,
            "success",
            StringComparison.Ordinal);
        var searchContext = CurrentSearchContext();
        var createClientForm = !profileClientId.HasValue
            && searchResult is { Status: SearchClientsStatus.Success }
            && searchResult.Items.Count == 0
            && searchResult.AllowedActions.IsAllowed(ClientSearchActionKeys.CreateClient)
                ? CreateClientFormViewModel.FromSearchContext(searchContext)
                : null;

        return new ReceptionWorkspaceViewModel(
            Query?.Trim(),
            Mode,
            IncludeInactive,
            PageCursor,
            searchResult,
            createClientForm,
            ClientProfileViewModel.FromResult(
                profileResult,
                searchContext,
                operationMessage,
                operationSucceeded));
    }

    private ReceptionSearchContext CurrentSearchContext()
    {
        return new ReceptionSearchContext(
            Query?.Trim(),
            Mode,
            IncludeInactive,
            PageCursor);
    }

    private void ApplySearchContext(UpdateClientFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(CreateClientFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(CardAssignmentFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private bool IsHtmxRequest()
    {
        return string.Equals(
            Request.Headers["HX-Request"],
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult RedirectToCanonicalPage(Guid? clientId)
    {
        return RedirectToPage(new
        {
            q = Query,
            mode = Mode == ClientSearchMode.Auto ? (ClientSearchMode?)null : Mode,
            includeInactive = IncludeInactive ? true : (bool?)null,
            pageCursor = PageCursor,
            clientId,
        });
    }

    private void SetHtmxPushUrl(Guid? clientId)
    {
        var url = Url.Page(
            "/Reception/Index",
            values: new
            {
                q = Query?.Trim(),
                mode = Mode == ClientSearchMode.Auto ? (ClientSearchMode?)null : Mode,
                includeInactive = IncludeInactive ? true : (bool?)null,
                pageCursor = PageCursor,
                clientId,
            })
            ?? throw new InvalidOperationException("Could not generate the reception dashboard URL.");

        Response.Headers["HX-Push-Url"] = url;
    }
}
