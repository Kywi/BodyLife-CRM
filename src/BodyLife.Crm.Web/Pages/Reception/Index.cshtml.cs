using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Reception;

public sealed class IndexModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<SearchClientsQuery, SearchClientsResult> searchClients,
    IBodyLifeQueryHandler<GetClientProfileQuery, GetClientProfileResult> getClientProfile,
    IBodyLifeQueryHandler<
        GetMembershipTypesForIssueQuery,
        GetMembershipTypesForIssueResult> getMembershipTypesForIssue,
    IBodyLifeQueryHandler<
        PreviewIssueMembershipQuery,
        PreviewIssueMembershipResult> previewIssueMembership,
    IBodyLifeQueryHandler<
        GetMarkVisitOptionsQuery,
        GetMarkVisitOptionsResult> getMarkVisitOptions,
    IBodyLifeQueryHandler<
        FindClientDuplicateCandidatesQuery,
        IReadOnlyList<ClientDuplicateCandidate>> findDuplicateCandidates,
    IBodyLifeCommandHandler<CreateClientCommand> createClient,
    IBodyLifeCommandHandler<UpdateClientCommand> updateClient,
    IBodyLifeCommandHandler<AssignOrChangeCardCommand> assignOrChangeCard,
    IBodyLifeCommandHandler<IssueMembershipCommand> issueMembership,
    IBodyLifeCommandHandler<AddFreezeCommand> addFreeze,
    IBodyLifeCommandHandler<CancelFreezeCommand> cancelFreeze,
    IBodyLifeCommandHandler<MarkVisitCommand> markVisit,
    IBodyLifeCommandHandler<CreatePaymentCommand> createPayment,
    IBodyLifeCommandHandler<CorrectPaymentCommand> correctPayment,
    IBodyLifeCommandHandler<CancelVisitCommand> cancelVisit,
    TimeProvider timeProvider)
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

    [BindProperty(SupportsGet = true, Name = "correctPaymentId")]
    public Guid? CorrectPaymentId { get; set; }

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
            new GetClientProfileQuery(
                actor,
                ClientId ?? Guid.Empty,
                IncludeHistory: true,
                RequiredPaymentId: CorrectPaymentId),
            cancellationToken);
        SetHtmxPushUrl(result.Profile?.ClientId ?? ClientId);

        return Partial(
            "_ClientProfile",
            await BuildProfileViewModelAsync(
                result,
                CurrentSearchContext(),
                cancellationToken));
    }

    public async Task<IActionResult> OnGetIssueMembershipPreviewAsync(
        IssueMembershipFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        ApplySearchContext(form);

        if (!IsHtmxRequest())
        {
            return RedirectToCanonicalPage(form.ClientId);
        }

        var issueForm = await BuildIssueMembershipFormFromInputAsync(
            form,
            errors: [],
            cancellationToken);
        return Partial("_IssueMembershipForm", issueForm);
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

    public async Task<IActionResult> OnPostIssueMembershipAsync(
        IssueMembershipFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var adapterErrors = ValidateIssueMembershipForm(form);
        if (adapterErrors.Count > 0)
        {
            return await RenderIssueMembershipErrorAsync(
                form,
                adapterErrors,
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        var payment = form.IncludePayment
            ? new MembershipIssuePayment(
                new Money(
                    form.PaymentAmount!.Value,
                    IssueMembershipFormViewModel.Currency),
                PaymentContext.MembershipSale)
            : null;
        var command = new IssueMembershipCommand(
            requestContextResolver.CreateCommandEnvelope(
                idempotencyKey: form.IdempotencyKey,
                comment: form.Comment),
            form.ClientId,
            form.MembershipTypeId!.Value,
            form.StartDate!.Value,
            form.NegativeHandlingDecision,
            Payment: payment);
        var result = await issueMembership.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulIssueMembershipAsync(
                form,
                result,
                cancellationToken);
        }

        return await RenderIssueMembershipErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalIssueRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostMarkVisitAsync(
        MarkVisitFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (form.VisitKind is null)
        {
            return await RenderMarkVisitErrorAsync(
                form,
                [
                    new CommandError(
                        CommandErrorCode.ValidationFailed,
                        "Choose membership, one-off or trial for this Visit.",
                        "visitKind"),
                ],
                forceCanonicalRefresh: false,
                cancellationToken: cancellationToken);
        }

        var command = new MarkVisitCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: form.OccurredAt,
                idempotencyKey: form.IdempotencyKey,
                comment: form.Comment),
            form.ClientId,
            form.VisitKind.Value,
            form.MembershipId,
            form.Acknowledgements ?? []);
        var result = await markVisit.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulMarkVisitAsync(form, result, cancellationToken);
        }

        return await RenderMarkVisitErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalVisitRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostAddFreezeAsync(
        AddFreezeFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var adapterErrors = ValidateAddFreezeForm(form);
        if (adapterErrors.Count > 0)
        {
            return await RenderAddFreezeErrorAsync(
                form,
                adapterErrors,
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        var command = new AddFreezeCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason,
                comment: form.Comment),
            form.ClientId,
            form.MembershipId!.Value,
            new DateRange(form.StartDate!.Value, form.EndDate!.Value));
        var result = await addFreeze.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulAddFreezeAsync(
                form,
                result,
                cancellationToken);
        }

        return await RenderAddFreezeErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalFreezeRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelVisitAsync(
        CancelVisitFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (!form.Confirmed)
        {
            return await RenderCancelVisitErrorAsync(
                form,
                [
                    new CommandError(
                        CommandErrorCode.ValidationFailed,
                        "Confirm that this Visit should be canceled.",
                        "confirmed"),
                ],
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        var command = new CancelVisitCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason,
                comment: form.Comment),
            form.VisitId);
        var result = await cancelVisit.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCancelVisitAsync(form, result, cancellationToken);
        }

        return await RenderCancelVisitErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalCancelVisitRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelFreezeAsync(
        CancelFreezeFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        if (!form.Confirmed)
        {
            return await RenderCancelFreezeErrorAsync(
                form,
                [
                    new CommandError(
                        CommandErrorCode.ValidationFailed,
                        "Confirm that this Freeze should be canceled.",
                        "confirmed"),
                ],
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        var command = new CancelFreezeCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason,
                comment: form.Comment),
            form.FreezeId);
        var result = await cancelFreeze.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCancelFreezeAsync(
                form,
                result,
                cancellationToken);
        }

        return await RenderCancelFreezeErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalCancelFreezeRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCreatePaymentAsync(
        AddPaymentFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var now = timeProvider.GetUtcNow();
        var adapterErrors = ValidateAddPaymentForm(form, now, out var occurredAt);
        if (adapterErrors.Count > 0)
        {
            return await RenderAddPaymentErrorAsync(
                form,
                adapterErrors,
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        var command = new CreatePaymentCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: occurredAt,
                idempotencyKey: form.IdempotencyKey,
                comment: form.Comment),
            form.ClientId,
            form.MembershipId,
            new Money(form.Amount!.Value, AddPaymentFormViewModel.Currency),
            form.PaymentContext!.Value);
        var result = await createPayment.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulPaymentAsync(form, result, cancellationToken);
        }

        return await RenderAddPaymentErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalPaymentRefresh(result.Errors),
            cancellationToken);
    }

    public async Task<IActionResult> OnPostCorrectPaymentAsync(
        CorrectPaymentFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var adapterErrors = ValidateCorrectPaymentForm(
            form,
            out var replacementOccurredAt);
        if (adapterErrors.Count > 0)
        {
            return await RenderCorrectPaymentErrorAsync(
                form,
                adapterErrors,
                forceCanonicalRefresh: false,
                cancellationToken);
        }

        PaymentReplacement? replacement = null;
        if (form.Mode == PaymentCorrectionMode.Replace)
        {
            replacement = new PaymentReplacement(
                form.ReplacementMembershipId,
                new Money(
                    form.ReplacementAmount!.Value,
                    CorrectPaymentFormViewModel.Currency),
                form.ReplacementPaymentContext!.Value,
                replacementOccurredAt,
                form.ReplacementComment);
        }

        var command = new CorrectPaymentCommand(
            requestContextResolver.CreateCommandEnvelope(
                occurredAt: timeProvider.GetUtcNow(),
                idempotencyKey: form.IdempotencyKey,
                reason: form.Reason,
                comment: form.Comment),
            form.OriginalPaymentId,
            form.Mode!.Value,
            replacement);
        var result = await correctPayment.ExecuteAsync(command, cancellationToken);

        if (result.Status == CommandStatus.Success)
        {
            return await RenderSuccessfulCorrectPaymentAsync(
                form,
                result,
                cancellationToken);
        }

        return await RenderCorrectPaymentErrorAsync(
            form,
            result.Errors,
            RequiresCanonicalCorrectPaymentRefresh(result.Errors),
            cancellationToken);
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

    private async Task<IActionResult> RenderSuccessfulIssueMembershipAsync(
        IssueMembershipFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } membershipId
            || membershipId.Type != IssueMembershipCommand.PrimaryEntityType
            || membershipId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != IssueMembershipCommand.CanonicalRereadEntityType
            || rereadTarget.Value != form.ClientId)
        {
            throw new InvalidOperationException(
                "IssueMembership did not return the expected Membership and canonical Client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var outcome = form.IncludePayment
            ? "Membership issued with cash payment"
            : "Membership issued";
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

    private async Task<IActionResult> RenderSuccessfulMarkVisitAsync(
        MarkVisitFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } visitId
            || visitId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Value != form.ClientId)
        {
            throw new InvalidOperationException(
                "MarkVisit did not return the expected Visit and canonical Client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var message = result.AuditEntryId is { } auditEntryId
            ? $"Visit marked. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : "Visit marked.";

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

    private async Task<IActionResult> RenderSuccessfulAddFreezeAsync(
        AddFreezeFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } freezeId
            || freezeId.Type != AddFreezeCommand.PrimaryEntityType
            || freezeId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != AddFreezeCommand.CanonicalRereadEntityType
            || rereadTarget.Value != form.ClientId
            || !result.RelatedEntityIds.Any(entityId =>
                entityId.Type == AddFreezeCommand.MembershipEntityType
                && entityId.Value == form.MembershipId))
        {
            throw new InvalidOperationException(
                "AddFreeze did not return the expected Freeze, Membership and canonical Client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var message = result.AuditEntryId is { } auditEntryId
            ? $"Freeze added. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : "Freeze added.";

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

    private async Task<IActionResult> RenderSuccessfulCancelFreezeAsync(
        CancelFreezeFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } cancellationId
            || cancellationId.Type != CancelFreezeCommand.PrimaryEntityType
            || cancellationId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != CancelFreezeCommand.CanonicalRereadEntityType
            || rereadTarget.Value == Guid.Empty
            || !result.RelatedEntityIds.Any(entityId =>
                entityId.Type == CancelFreezeCommand.SourceFreezeEntityType
                && entityId.Value == form.FreezeId))
        {
            throw new InvalidOperationException(
                "CancelFreeze did not return the expected cancellation, source Freeze and canonical Client reread target.");
        }

        ApplySearchContext(form);
        if (rereadTarget.Value != form.ClientId)
        {
            Query = null;
            Mode = ClientSearchMode.Auto;
            IncludeInactive = false;
            PageCursor = null;
        }

        ClientId = rereadTarget.Value;
        var outcome = result.ChangedAfterClose
            ? "Freeze canceled after reconciled day"
            : "Freeze canceled";
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

    private async Task<IActionResult> RenderSuccessfulCancelVisitAsync(
        CancelVisitFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } cancellationId
            || cancellationId.Type != CancelVisitCommand.PrimaryEntityType
            || cancellationId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != CancelVisitCommand.CanonicalRereadEntityType
            || rereadTarget.Value == Guid.Empty
            || !result.RelatedEntityIds.Any(entityId =>
                entityId.Type == CancelVisitCommand.SourceVisitEntityType
                && entityId.Value == form.VisitId))
        {
            throw new InvalidOperationException(
                "CancelVisit did not return the expected cancellation, source Visit and canonical Client reread target.");
        }

        ApplySearchContext(form);
        if (rereadTarget.Value != form.ClientId)
        {
            Query = null;
            Mode = ClientSearchMode.Auto;
            IncludeInactive = false;
            PageCursor = null;
        }

        ClientId = rereadTarget.Value;
        var outcome = result.ChangedAfterClose
            ? "Visit canceled after reconciled day"
            : "Visit canceled";
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

    private async Task<IActionResult> RenderSuccessfulPaymentAsync(
        AddPaymentFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        if (result.PrimaryEntityId is not { } paymentId
            || paymentId.Type != CreatePaymentCommand.PrimaryEntityType
            || paymentId.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != CreatePaymentCommand.CanonicalRereadEntityType
            || rereadTarget.Value != form.ClientId)
        {
            throw new InvalidOperationException(
                "CreatePayment did not return the expected Payment and canonical Client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var message = result.AuditEntryId is { } auditEntryId
            ? $"Payment added. Audit reference {auditEntryId.Value.ToString("N")[..8]}."
            : "Payment added.";

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

    private async Task<IActionResult> RenderSuccessfulCorrectPaymentAsync(
        CorrectPaymentFormInput form,
        CommandResult result,
        CancellationToken cancellationToken)
    {
        var expectedPrimaryType = form.Mode == PaymentCorrectionMode.Replace
            ? CorrectPaymentCommand.CorrectionEntityType
            : CorrectPaymentCommand.CancellationEntityType;
        var relatedPayments = result.RelatedEntityIds
            .Where(entityId => entityId.Type == CorrectPaymentCommand.PaymentEntityType)
            .ToArray();
        var hasExpectedRelatedPayments = form.Mode == PaymentCorrectionMode.Replace
            ? relatedPayments.Length == 2
                && relatedPayments.Any(entityId => entityId.Value == form.OriginalPaymentId)
                && relatedPayments.Any(entityId =>
                    entityId.Value != Guid.Empty
                    && entityId.Value != form.OriginalPaymentId)
            : relatedPayments.Length == 1
                && relatedPayments[0].Value == form.OriginalPaymentId;

        if (result.PrimaryEntityId is not { } primaryEntity
            || primaryEntity.Type != expectedPrimaryType
            || primaryEntity.Value == Guid.Empty
            || result.RereadTargetId is not { } rereadTarget
            || rereadTarget.Type != CorrectPaymentCommand.CanonicalRereadEntityType
            || rereadTarget.Value != form.ClientId
            || !hasExpectedRelatedPayments)
        {
            throw new InvalidOperationException(
                "CorrectPayment did not return the expected correction facts, Payment references and canonical Client reread target.");
        }

        ApplySearchContext(form);
        ClientId = rereadTarget.Value;
        var outcome = form.Mode == PaymentCorrectionMode.Replace
            ? "Payment corrected"
            : "Payment canceled";
        if (result.ChangedAfterClose)
        {
            outcome += " after reconciled day";
        }

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

    private async Task<IActionResult> RenderIssueMembershipErrorAsync(
        IssueMembershipFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        var issueForm = await BuildIssueMembershipFormFromInputAsync(
            form,
            errors,
            cancellationToken);

        if (IsHtmxRequest() && !forceCanonicalRefresh)
        {
            return Partial("_IssueMembershipForm", issueForm);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        if (Workspace.Profile.Result?.Profile?.ClientId == form.ClientId)
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    IssueMembershipForm = issueForm,
                },
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

    private async Task<IActionResult> RenderMarkVisitErrorAsync(
        MarkVisitFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        var markVisitForm = await BuildMarkVisitErrorFormAsync(
            form,
            errors,
            cancellationToken);

        if (IsHtmxRequest() && !forceCanonicalRefresh)
        {
            return Partial("_MarkVisitForm", markVisitForm);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        if (Workspace.Profile.Result?.Profile?.ClientId == form.ClientId)
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with { MarkVisitForm = markVisitForm },
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

    private async Task<IActionResult> RenderCancelVisitErrorAsync(
        CancelVisitFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;

        if (IsHtmxRequest() && !forceCanonicalRefresh)
        {
            var errorForm = await BuildCancelVisitErrorFormAsync(
                form,
                errors,
                cancellationToken);
            if (errorForm is not null)
            {
                return Partial("_CancelVisitForm", errorForm);
            }

            forceCanonicalRefresh = true;
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        var currentForm = Workspace.Profile.CancelVisitForms
            .SingleOrDefault(candidate => candidate.Input.VisitId == form.VisitId);
        if (currentForm is not null)
        {
            var errorForm = forceCanonicalRefresh
                ? CancelVisitFormViewModel.FromCanonicalRefresh(form, currentForm, errors)
                : CancelVisitFormViewModel.FromSubmission(form, currentForm.Visit, errors);
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    CancelVisitForms = Workspace.Profile.CancelVisitForms
                        .Select(candidate => candidate.Input.VisitId == form.VisitId
                            ? errorForm
                            : candidate)
                        .ToArray(),
                },
            };
        }
        else
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    OperationMessage = errors
                        .Select(CancelVisitFormViewModel.DisplayError)
                        .FirstOrDefault()
                        ?? "Visit cancellation could not be completed.",
                    OperationSucceeded = false,
                },
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

    private async Task<IActionResult> RenderAddPaymentErrorAsync(
        AddPaymentFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        var addPaymentForm = await BuildAddPaymentErrorFormAsync(
            form,
            errors,
            cancellationToken);

        if (IsHtmxRequest() && !forceCanonicalRefresh && addPaymentForm is not null)
        {
            return Partial("_AddPaymentForm", addPaymentForm);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        if (addPaymentForm is not null
            && Workspace.Profile.Result?.Profile?.ClientId == form.ClientId)
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with { AddPaymentForm = addPaymentForm },
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

    private async Task<IActionResult> RenderAddFreezeErrorAsync(
        AddFreezeFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;
        var addFreezeForm = await BuildAddFreezeErrorFormAsync(
            form,
            errors,
            cancellationToken);

        if (IsHtmxRequest() && !forceCanonicalRefresh && addFreezeForm is not null)
        {
            return Partial("_AddFreezeForm", addFreezeForm);
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        if (addFreezeForm is not null
            && Workspace.Profile.Result?.Profile?.ClientId == form.ClientId)
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with { AddFreezeForm = addFreezeForm },
            };
        }
        else
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    OperationMessage = errors
                        .Select(AddFreezeFormViewModel.DisplayError)
                        .FirstOrDefault()
                        ?? "Freeze could not be added.",
                    OperationSucceeded = false,
                },
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

    private async Task<IActionResult> RenderCancelFreezeErrorAsync(
        CancelFreezeFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;

        if (IsHtmxRequest() && !forceCanonicalRefresh)
        {
            var errorForm = await BuildCancelFreezeErrorFormAsync(
                form,
                errors,
                cancellationToken);
            if (errorForm is not null)
            {
                return Partial("_CancelFreezeForm", errorForm);
            }

            forceCanonicalRefresh = true;
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        var currentForm = Workspace.Profile.CancelFreezeForms
            .SingleOrDefault(candidate => candidate.Input.FreezeId == form.FreezeId);
        if (currentForm is not null)
        {
            var errorForm = forceCanonicalRefresh
                ? CancelFreezeFormViewModel.FromCanonicalRefresh(form, currentForm, errors)
                : CancelFreezeFormViewModel.FromSubmission(
                    form,
                    currentForm.Membership,
                    currentForm.Freeze,
                    errors);
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    CancelFreezeForms = Workspace.Profile.CancelFreezeForms
                        .Select(candidate => candidate.Input.FreezeId == form.FreezeId
                            ? errorForm
                            : candidate)
                        .ToArray(),
                },
            };
        }
        else
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    OperationMessage = errors
                        .Select(CancelFreezeFormViewModel.DisplayError)
                        .FirstOrDefault()
                        ?? "Freeze cancellation could not be completed.",
                    OperationSucceeded = false,
                },
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

    private async Task<IActionResult> RenderCorrectPaymentErrorAsync(
        CorrectPaymentFormInput form,
        IReadOnlyList<CommandError> errors,
        bool forceCanonicalRefresh,
        CancellationToken cancellationToken)
    {
        ApplySearchContext(form);
        ClientId = form.ClientId;

        if (IsHtmxRequest() && !forceCanonicalRefresh)
        {
            var errorForm = await BuildCorrectPaymentErrorFormAsync(
                form,
                errors,
                cancellationToken);
            if (errorForm is not null)
            {
                return Partial("_CorrectPaymentForm", errorForm);
            }

            forceCanonicalRefresh = true;
        }

        Workspace = await BuildWorkspaceAsync(cancellationToken);
        var currentForm = Workspace.Profile.CorrectPaymentForms.SingleOrDefault(candidate =>
            candidate.Input.OriginalPaymentId == form.OriginalPaymentId);
        var profile = Workspace.Profile.Result?.Profile;
        if (currentForm is not null && profile is not null)
        {
            var errorForm = forceCanonicalRefresh
                ? CorrectPaymentFormViewModel.FromCanonicalRefresh(
                    form,
                    currentForm,
                    profile,
                    errors)
                : CorrectPaymentFormViewModel.FromSubmission(
                    form,
                    currentForm.Payment,
                    profile,
                    errors);
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    CorrectPaymentForms = Workspace.Profile.CorrectPaymentForms
                        .Select(candidate =>
                            candidate.Input.OriginalPaymentId == form.OriginalPaymentId
                                ? errorForm
                                : candidate)
                        .ToArray(),
                },
            };
        }
        else
        {
            Workspace = Workspace with
            {
                Profile = Workspace.Profile with
                {
                    OperationMessage = errors
                        .Select(CorrectPaymentFormViewModel.DisplayError)
                        .FirstOrDefault()
                        ?? "Payment correction could not be completed.",
                    OperationSucceeded = false,
                },
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

    private async Task<IssueMembershipFormViewModel> BuildInitialIssueMembershipFormAsync(
        Guid clientId,
        DateOnly startDate,
        ReceptionSearchContext searchContext,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var membershipTypesResult = await getMembershipTypesForIssue.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(actor),
            cancellationToken);
        var selectedType = membershipTypesResult.Items.FirstOrDefault();
        var previewResult = selectedType is null
            ? null
            : await previewIssueMembership.ExecuteAsync(
                new PreviewIssueMembershipQuery(
                    actor,
                    clientId,
                    selectedType.MembershipTypeId,
                    startDate),
                cancellationToken);

        return IssueMembershipFormViewModel.FromInitialQueries(
            clientId,
            startDate,
            membershipTypesResult,
            previewResult,
            searchContext);
    }

    private async Task<IssueMembershipFormViewModel> BuildIssueMembershipFormFromInputAsync(
        IssueMembershipFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var membershipTypesResult = await getMembershipTypesForIssue.ExecuteAsync(
            new GetMembershipTypesForIssueQuery(actor),
            cancellationToken);
        PreviewIssueMembershipResult? previewResult = null;

        if (form.MembershipTypeId is { } membershipTypeId
            && membershipTypeId != Guid.Empty
            && form.StartDate is { } startDate
            && startDate != default)
        {
            previewResult = await previewIssueMembership.ExecuteAsync(
                new PreviewIssueMembershipQuery(
                    actor,
                    form.ClientId,
                    membershipTypeId,
                    startDate,
                    form.NegativeHandlingDecision),
                cancellationToken);

            if (form.NegativeHandlingDecision is not null
                && previewResult is
                {
                    Status: PreviewIssueMembershipStatus.ValidationFailed,
                    ErrorField: "negativeHandlingDecision",
                })
            {
                form.NegativeHandlingDecision = null;
                previewResult = await previewIssueMembership.ExecuteAsync(
                    new PreviewIssueMembershipQuery(
                        actor,
                        form.ClientId,
                        membershipTypeId,
                        startDate),
                    cancellationToken);
            }
        }

        return IssueMembershipFormViewModel.FromSubmission(
            form,
            membershipTypesResult,
            previewResult,
            errors);
    }

    private async Task<MarkVisitFormViewModel> BuildMarkVisitErrorFormAsync(
        MarkVisitFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var occurredAt = form.OccurredAt == default
            ? timeProvider.GetUtcNow()
            : form.OccurredAt;
        var optionsResult = await getMarkVisitOptions.ExecuteAsync(
            new GetMarkVisitOptionsQuery(actor, form.ClientId, occurredAt),
            cancellationToken);

        return MarkVisitFormViewModel.FromSubmission(form, optionsResult, errors);
    }

    private async Task<CancelVisitFormViewModel?> BuildCancelVisitErrorFormAsync(
        CancelVisitFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var profileResult = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                form.ClientId,
                IncludeHistory: true),
            cancellationToken);
        var visit = profileResult.Profile?.RecentVisits?.Items.SingleOrDefault(row =>
            row.VisitId == form.VisitId
            && row.ClientId == form.ClientId
            && row.Status == ClientVisitRowStatus.Active
            && row.AllowedActions.IsAllowed(VisitActionKeys.Cancel));

        return visit is null
            ? null
            : CancelVisitFormViewModel.FromSubmission(form, visit, errors);
    }

    private static bool RequiresCanonicalIssueRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is
            CommandErrorCode.PermissionDenied
            or CommandErrorCode.NotFound
            or CommandErrorCode.MembershipTypeInactive
            or CommandErrorCode.RecalculationFailed
            or CommandErrorCode.ConcurrencyConflict
            or CommandErrorCode.StaleState);
    }

    private static IReadOnlyList<CommandError> ValidateIssueMembershipForm(
        IssueMembershipFormInput form)
    {
        var errors = new List<CommandError>();

        if (form.ClientId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Client id is required.",
                "clientId"));
        }

        if (!form.MembershipTypeId.HasValue
            || form.MembershipTypeId.Value == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Choose an active membership type.",
                "membershipTypeId"));
        }

        if (!form.StartDate.HasValue || form.StartDate.Value == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Start date is required.",
                "startDate"));
        }

        if (form.NegativeHandlingDecision is { } decision
            && !Enum.IsDefined(decision))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Negative handling decision is not supported.",
                "negativeHandlingDecision"));
        }

        if (form.IncludePayment
            && (form.PaymentAmount is null || form.PaymentAmount <= 0))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Payment amount must be greater than zero.",
                "payment.amount"));
        }

        if (form.Comment?.Trim().Length > IssueMembershipFormViewModel.CommentMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Comment must be {IssueMembershipFormViewModel.CommentMaxLength} characters or fewer.",
                "envelope.comment"));
        }

        return errors.AsReadOnly();
    }

    private static bool RequiresCanonicalVisitRefresh(IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code != CommandErrorCode.ValidationFailed);
    }

    private static bool RequiresCanonicalCancelVisitRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is not
            (CommandErrorCode.ValidationFailed or CommandErrorCode.ReasonRequired));
    }

    private async Task<AddFreezeFormViewModel?> BuildAddFreezeErrorFormAsync(
        AddFreezeFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                form.ClientId,
                IncludeHistory: true),
            cancellationToken);

        if (result is not
            {
                Status: GetClientProfileStatus.Success,
                Profile: { } profile,
            }
            || !profile.AllowedActions.IsAllowed(FreezeActionKeys.Add))
        {
            return null;
        }

        var errorForm = AddFreezeFormViewModel.FromSubmission(form, profile, errors);
        return errorForm.MembershipOptions.Count > 0
            ? errorForm
            : null;
    }

    private static bool RequiresCanonicalFreezeRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is
            CommandErrorCode.PermissionDenied
            or CommandErrorCode.NotFound
            or CommandErrorCode.MembershipNotEligible
            or CommandErrorCode.FreezeConflictsWithVisit
            or CommandErrorCode.RecalculationFailed
            or CommandErrorCode.ConcurrencyConflict
            or CommandErrorCode.StaleState);
    }

    private async Task<CancelFreezeFormViewModel?> BuildCancelFreezeErrorFormAsync(
        CancelFreezeFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                form.ClientId,
                IncludeHistory: true),
            cancellationToken);

        if (result is not
            {
                Status: GetClientProfileStatus.Success,
                Profile: { } profile,
            }
            || !profile.AllowedActions.IsAllowed(FreezeActionKeys.Cancel))
        {
            return null;
        }

        var candidate = profile.Membership.Timeline
            .SelectMany(membership => membership.ExtensionExplanations
                .Where(explanation =>
                    explanation.SourceKind == MembershipExtensionSourceKind.Freeze
                    && explanation.Status == MembershipExtensionSourceStatus.Active
                    && explanation.SourceId == form.FreezeId)
                .Select(explanation => new
                {
                    Membership = membership,
                    Freeze = explanation,
                }))
            .SingleOrDefault();

        return candidate is null
            ? null
            : CancelFreezeFormViewModel.FromSubmission(
                form,
                candidate.Membership,
                candidate.Freeze,
                errors);
    }

    private static bool RequiresCanonicalCancelFreezeRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is not
            (CommandErrorCode.ValidationFailed or CommandErrorCode.ReasonRequired));
    }

    private async Task<AddPaymentFormViewModel?> BuildAddPaymentErrorFormAsync(
        AddPaymentFormInput form,
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
            && profile.AllowedActions.IsAllowed(PaymentActionKeys.Create)
                ? AddPaymentFormViewModel.FromSubmission(form, profile, errors)
                : null;
    }

    private static bool RequiresCanonicalPaymentRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code != CommandErrorCode.ValidationFailed);
    }

    private async Task<CorrectPaymentFormViewModel?> BuildCorrectPaymentErrorFormAsync(
        CorrectPaymentFormInput form,
        IReadOnlyList<CommandError> errors,
        CancellationToken cancellationToken)
    {
        var actor = requestContextResolver.Require().Actor;
        var result = await getClientProfile.ExecuteAsync(
            new GetClientProfileQuery(
                actor,
                form.ClientId,
                IncludeHistory: true),
            cancellationToken);
        var profile = result.Profile;
        var payment = profile?.RecentPayments?.Items.SingleOrDefault(row =>
            row.PaymentId == form.OriginalPaymentId
            && row.ClientId == form.ClientId
            && row.Status == ClientPaymentRowStatus.Active
            && row.PaymentContext != PaymentContext.NegativeClosure
            && row.AllowedActions.IsAllowed(PaymentActionKeys.Correct));

        return profile is null || payment is null
            ? null
            : CorrectPaymentFormViewModel.FromSubmission(
                form,
                payment,
                profile,
                errors);
    }

    private static bool RequiresCanonicalCorrectPaymentRefresh(
        IReadOnlyList<CommandError> errors)
    {
        return errors.Any(error => error.Code is not
            (CommandErrorCode.ValidationFailed or CommandErrorCode.ReasonRequired));
    }

    private static IReadOnlyList<CommandError> ValidateCorrectPaymentForm(
        CorrectPaymentFormInput form,
        out DateTimeOffset replacementOccurredAt)
    {
        var errors = new List<CommandError>();
        replacementOccurredAt = default;

        if (form.ClientId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Client id is required.",
                "clientId"));
        }

        if (form.OriginalPaymentId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Original Payment id is required.",
                "originalPaymentId"));
        }

        if (form.Mode is not { } mode || !Enum.IsDefined(mode))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Choose replace or cancel mode.",
                "mode"));
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ReasonRequired,
                "Payment correction reason is required.",
                "reason"));
        }
        else if (form.Reason.Trim().Length > CorrectPaymentFormViewModel.ReasonMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Reason must be {CorrectPaymentFormViewModel.ReasonMaxLength} characters or fewer.",
                "reason"));
        }

        if (form.Comment?.Trim().Length > CorrectPaymentFormViewModel.CommentMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Correction comment must be {CorrectPaymentFormViewModel.CommentMaxLength} characters or fewer.",
                "comment"));
        }

        if (!form.Confirmed)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Confirm that this Payment should be corrected.",
                "confirmed"));
        }

        if (form.Mode != PaymentCorrectionMode.Replace)
        {
            return errors.AsReadOnly();
        }

        if (form.ReplacementAmount is null || form.ReplacementAmount <= 0)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Replacement Payment amount must be greater than zero.",
                "replacement.amount"));
        }

        if (form.ReplacementPaymentContext is not { } paymentContext
            || !CorrectPaymentFormViewModel.SupportedContexts.Contains(paymentContext))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Choose a supported replacement Payment context.",
                "replacement.paymentContext"));
        }

        if (form.ReplacementMembershipId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Replacement Membership id must be non-empty when supplied.",
                "replacement.membershipId"));
        }

        if (form.ReplacementOccurredAtUtc is not { } occurredAtUtc)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Replacement Payment occurred time is required.",
                "replacement.occurredAt"));
        }
        else
        {
            replacementOccurredAt = new DateTimeOffset(
                DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Unspecified),
                TimeSpan.Zero);
        }

        if (form.ReplacementComment?.Trim().Length
            > CorrectPaymentFormViewModel.CommentMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Replacement comment must be {CorrectPaymentFormViewModel.CommentMaxLength} characters or fewer.",
                "replacement.comment"));
        }

        return errors.AsReadOnly();
    }

    private static IReadOnlyList<CommandError> ValidateAddFreezeForm(
        AddFreezeFormInput form)
    {
        var errors = new List<CommandError>();

        if (form.ClientId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Client id is required.",
                "clientId"));
        }

        if (form.MembershipId is null || form.MembershipId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Choose a Membership for this Freeze.",
                "membershipId"));
        }

        if (form.StartDate is null || form.StartDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Freeze start date is required.",
                "range.startDate"));
        }

        if (form.EndDate is null || form.EndDate == default)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Freeze end date is required.",
                "range.endDate"));
        }
        else if (form.StartDate is { } startDate && form.EndDate < startDate)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Freeze end date must be on or after the start date.",
                "range"));
        }

        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ReasonRequired,
                "Freeze reason is required.",
                "reason"));
        }
        else if (form.Reason.Trim().Length > AddFreezeFormViewModel.ReasonMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Freeze reason must be {AddFreezeFormViewModel.ReasonMaxLength} characters or fewer.",
                "reason"));
        }

        if (form.Comment?.Trim().Length > AddFreezeFormViewModel.CommentMaxLength)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                $"Freeze comment must be {AddFreezeFormViewModel.CommentMaxLength} characters or fewer.",
                "comment"));
        }

        return errors.AsReadOnly();
    }

    private static IReadOnlyList<CommandError> ValidateAddPaymentForm(
        AddPaymentFormInput form,
        DateTimeOffset now,
        out DateTimeOffset occurredAt)
    {
        var errors = new List<CommandError>();
        occurredAt = default;

        if (form.Amount is null || form.Amount <= 0)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Payment amount must be greater than zero.",
                "amount"));
        }

        if (form.PaymentContext is not { } paymentContext
            || !AddPaymentFormViewModel.SupportedContexts.Contains(paymentContext))
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Choose a supported Payment context.",
                "paymentContext"));
        }

        if (form.MembershipId == Guid.Empty)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Membership id must be a non-empty identifier when supplied.",
                "membershipId"));
        }

        if (form.OccurredAtUtc is not { } occurredAtUtc)
        {
            errors.Add(new CommandError(
                CommandErrorCode.ValidationFailed,
                "Payment occurred time is required.",
                "occurredAt"));
        }
        else
        {
            occurredAt = new DateTimeOffset(
                DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Unspecified),
                TimeSpan.Zero);
            var occurredDate = DateOnly.FromDateTime(occurredAt.UtcDateTime);
            var currentDate = DateOnly.FromDateTime(now.UtcDateTime);

            if (occurredDate != currentDate)
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    "This form accepts normal same-day Payments only.",
                    "occurredAt"));
            }
            else if (occurredAt > now.AddMinutes(1))
            {
                errors.Add(new CommandError(
                    CommandErrorCode.ValidationFailed,
                    "Payment occurred time cannot be in the future.",
                    "occurredAt"));
            }
        }

        return errors.AsReadOnly();
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
                new GetClientProfileQuery(
                    actor,
                    profileClientId.Value,
                    IncludeHistory: true,
                    RequiredPaymentId: CorrectPaymentId),
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
        var profileViewModel = await BuildProfileViewModelAsync(
            profileResult,
            searchContext,
            cancellationToken,
            operationMessage,
            operationSucceeded);

        return new ReceptionWorkspaceViewModel(
            Query?.Trim(),
            Mode,
            IncludeInactive,
            PageCursor,
            searchResult,
            createClientForm,
            profileViewModel);
    }

    private async Task<ClientProfileViewModel> BuildProfileViewModelAsync(
        GetClientProfileResult? profileResult,
        ReceptionSearchContext searchContext,
        CancellationToken cancellationToken,
        string? operationMessage = null,
        bool operationSucceeded = false)
    {
        MarkVisitFormViewModel? markVisitForm = null;
        IssueMembershipFormViewModel? issueMembershipForm = null;
        AddPaymentFormViewModel? addPaymentForm = null;
        AddFreezeFormViewModel? addFreezeForm = null;
        IReadOnlyList<CancelFreezeFormViewModel> cancelFreezeForms = [];
        IReadOnlyList<CancelVisitFormViewModel> cancelVisitForms = [];
        IReadOnlyList<CorrectPaymentFormViewModel> correctPaymentForms = [];

        if (profileResult is
            {
                Status: GetClientProfileStatus.Success,
                Profile: { } profile,
            })
        {
            var occurredAt = timeProvider.GetUtcNow();
            var optionsResult = await getMarkVisitOptions.ExecuteAsync(
                new GetMarkVisitOptionsQuery(
                    requestContextResolver.Require().Actor,
                    profile.ClientId,
                    occurredAt),
                cancellationToken);
            markVisitForm = MarkVisitFormViewModel.FromQuery(
                profile.ClientId,
                occurredAt,
                optionsResult,
                searchContext);
            if (profile.AllowedActions.IsAllowed(MembershipActionKeys.Issue))
            {
                issueMembershipForm = await BuildInitialIssueMembershipFormAsync(
                    profile.ClientId,
                    DateOnly.FromDateTime(occurredAt.UtcDateTime),
                    searchContext,
                    cancellationToken);
            }
            if (profile.AllowedActions.IsAllowed(PaymentActionKeys.Create))
            {
                addPaymentForm = AddPaymentFormViewModel.FromProfile(
                    profile,
                    occurredAt,
                    searchContext);
            }
            if (profile.AllowedActions.IsAllowed(FreezeActionKeys.Add))
            {
                var candidate = AddFreezeFormViewModel.FromProfile(
                    profile,
                    searchContext);
                if (candidate.MembershipOptions.Count > 0)
                {
                    addFreezeForm = candidate;
                }
            }
            if (profile.AllowedActions.IsAllowed(FreezeActionKeys.Cancel))
            {
                cancelFreezeForms = profile.Membership.Timeline
                    .SelectMany(membership => membership.ExtensionExplanations
                        .Where(explanation =>
                            explanation.SourceKind == MembershipExtensionSourceKind.Freeze
                            && explanation.Status == MembershipExtensionSourceStatus.Active)
                        .Select(explanation => CancelFreezeFormViewModel.FromFreeze(
                            profile.ClientId,
                            membership,
                            explanation,
                            searchContext)))
                    .ToArray();
            }
            cancelVisitForms = profile.RecentVisits?.Items
                .Where(visit => visit.Status == ClientVisitRowStatus.Active
                    && visit.AllowedActions.IsAllowed(VisitActionKeys.Cancel))
                .Select(visit => CancelVisitFormViewModel.FromVisit(visit, searchContext))
                .ToArray() ?? [];
            correctPaymentForms = profile.RecentPayments?.Items
                .Where(payment => payment.Status == ClientPaymentRowStatus.Active
                    && payment.PaymentContext != PaymentContext.NegativeClosure
                    && payment.AllowedActions.IsAllowed(PaymentActionKeys.Correct))
                .Select(payment => CorrectPaymentFormViewModel.FromPayment(
                    payment,
                    profile,
                    searchContext) with
                {
                    IsOpen = payment.PaymentId == CorrectPaymentId,
                })
                .ToArray() ?? [];
        }

        return ClientProfileViewModel.FromResult(
            profileResult,
            searchContext,
            operationMessage,
            operationSucceeded,
            markVisitForm: markVisitForm,
            issueMembershipForm: issueMembershipForm,
            addPaymentForm: addPaymentForm,
            addFreezeForm: addFreezeForm,
            cancelFreezeForms: cancelFreezeForms,
            cancelVisitForms: cancelVisitForms,
            correctPaymentForms: correctPaymentForms);
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

    private void ApplySearchContext(IssueMembershipFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(MarkVisitFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(CancelVisitFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(AddPaymentFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(AddFreezeFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(CancelFreezeFormInput form)
    {
        Query = form.SearchQuery;
        Mode = form.SearchMode;
        IncludeInactive = form.SearchIncludeInactive;
        PageCursor = form.SearchPageCursor;
    }

    private void ApplySearchContext(CorrectPaymentFormInput form)
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
