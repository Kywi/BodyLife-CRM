using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class AssignOrChangeCardCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<AssignOrChangeCardCommand>
{
    private const string CommandName = "AssignOrChangeCard";
    private const string CurrentCardUniqueConstraint = "ux_client_card_assignments_current_card";
    private const string CurrentClientUniqueConstraint = "ux_client_card_assignments_current_client";

    public async Task<CommandResult> ExecuteAsync(
        AssignOrChangeCardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !ClientCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return ClientCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner, named Admin or shared Reception/Admin session is required.");
        }

        if (command.ClientId == Guid.Empty)
        {
            return ClientCommandSupport.ValidationError("Client id is required.", "clientId");
        }

        if (command.ExpectedCurrentCardAssignmentId == Guid.Empty)
        {
            return ClientCommandSupport.ValidationError(
                "Expected current card assignment id must be non-empty when supplied.",
                "expectedCurrentCardAssignmentId");
        }

        var envelopeValidation = ClientCommandSupport.ValidateAndNormalizeEnvelope(
            command.Envelope,
            "card assignment",
            out var normalizedEnvelope);

        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        var commandEnvelope = normalizedEnvelope!;
        var cardValidation = ValidateAndNormalizeCardIntent(
            command.NewCardNumber,
            command.ClearCurrentCard,
            out var newCardNumberRaw,
            out var newCardNumberNormalized);

        if (cardValidation is not null)
        {
            return cardValidation;
        }

        var recordedAt = timeProvider.GetUtcNow();
        var occurredAt = command.Envelope.OccurredAt?.ToUniversalTime() ?? recordedAt;
        var fingerprint = ClientCommandSupport.CreateAssignOrChangeCardFingerprint(
            command.Envelope,
            command.ClientId,
            command.ExpectedCurrentCardAssignmentId,
            newCardNumberNormalized,
            command.ClearCurrentCard);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (!await ClientCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    command.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The actor account or session is not active.");
            }

            var existingIdempotency = await ClientCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                commandEnvelope.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return ClientCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    command.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var client = await LockClientAsync(command.ClientId, cancellationToken);

            if (client is null)
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId");
            }

            var currentAssignment = await LockCurrentAssignmentAsync(
                command.ClientId,
                cancellationToken);

            if (currentAssignment?.Id != command.ExpectedCurrentCardAssignmentId)
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Current card assignment changed after the form was loaded. Refresh canonical state.",
                    "expectedCurrentCardAssignmentId");
            }

            if (command.ClearCurrentCard && currentAssignment is null)
            {
                return ClientCommandSupport.ValidationError(
                    "Client has no current card to clear.",
                    "clearCurrentCard");
            }

            var endReason = commandEnvelope.Reason ?? commandEnvelope.Comment;

            if (currentAssignment is not null && endReason is null)
            {
                return ClientCommandSupport.ValidationError(
                    "Reason or command comment is required when replacing, reissuing or clearing a current card.",
                    "reason");
            }

            if (currentAssignment is not null && occurredAt < currentAssignment.AssignedAt)
            {
                return ClientCommandSupport.ValidationError(
                    "Card change occurred_at cannot precede the current assignment time.",
                    "occurredAt");
            }

            if (newCardNumberNormalized is not null
                && await dbContext.Set<ClientCardAssignmentRecord>()
                    .AsNoTracking()
                    .AnyAsync(
                        assignment => assignment.IsCurrent
                            && assignment.ClientId != command.ClientId
                            && assignment.CardNumberNormalized == newCardNumberNormalized,
                        cancellationToken))
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.CardNumberAlreadyCurrent,
                    "Card number is already assigned to another current client.",
                    "newCardNumber");
            }

            var before = CardAssignmentSnapshot.From(currentAssignment);

            if (currentAssignment is not null)
            {
                currentAssignment.EndedAt = occurredAt;
                currentAssignment.EndedByAccountId = command.Envelope.Actor.AccountId.Value;
                currentAssignment.EndReason = endReason;
                currentAssignment.IsCurrent = false;

                // Release both partial unique indexes before a same-number reissue is inserted.
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var newAssignment = newCardNumberNormalized is null
                ? null
                : new ClientCardAssignmentRecord
                {
                    Id = Guid.NewGuid(),
                    ClientId = command.ClientId,
                    CardNumberRaw = newCardNumberRaw!,
                    CardNumberNormalized = newCardNumberNormalized,
                    AssignedAt = occurredAt,
                    AssignedByAccountId = command.Envelope.Actor.AccountId.Value,
                    EndedAt = null,
                    EndedByAccountId = null,
                    EndReason = null,
                    IsCurrent = true,
                };

            if (newAssignment is not null)
            {
                dbContext.Set<ClientCardAssignmentRecord>().Add(newAssignment);
            }

            var actionType = currentAssignment switch
            {
                null => ClientAuditActions.CardAssigned,
                _ when command.ClearCurrentCard => ClientAuditActions.CardCleared,
                _ => ClientAuditActions.CardChanged,
            };
            var after = CardAssignmentSnapshot.From(newAssignment);
            var auditEntryId = auditAppender.Append(
                command.Envelope,
                actionType,
                ClientAuditActions.EntityType,
                client.Id,
                recordedAt,
                relatedEntityRefs: new
                {
                    PreviousCardAssignmentId = currentAssignment?.Id,
                    CurrentCardAssignmentId = newAssignment?.Id,
                },
                beforeSummary: before,
                afterSummary: after);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                ClientCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    command.Envelope,
                    commandEnvelope,
                    recordedAt,
                    client.Id,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ClientCommandSupport.Success(client.Id, auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = ClientCommandSupport.FindPostgresException(exception);

            if (postgresException is null
                || !TryMapPostgresFailure(postgresException, out var errorResult))
            {
                throw;
            }

            await ClientCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            return errorResult;
        }
    }

    private async Task<ClientRecord?> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var clients = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.clients
                where id = {clientId}
                for update
                """)
            .ToArrayAsync(cancellationToken);
        return clients.SingleOrDefault();
    }

    private async Task<ClientCardAssignmentRecord?> LockCurrentAssignmentAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var assignments = await dbContext.Set<ClientCardAssignmentRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.client_card_assignments
                where client_id = {clientId}
                  and is_current
                for update
                """)
            .ToArrayAsync(cancellationToken);
        return assignments.SingleOrDefault();
    }

    private static CommandResult? ValidateAndNormalizeCardIntent(
        string? newCardNumber,
        bool clearCurrentCard,
        out string? newCardNumberRaw,
        out string? newCardNumberNormalized)
    {
        newCardNumberRaw = ClientCommandSupport.NormalizeOptional(newCardNumber);
        newCardNumberNormalized = null;

        if (clearCurrentCard && newCardNumberRaw is not null)
        {
            return ClientCommandSupport.ValidationError(
                "New card number cannot be supplied with explicit clear-card intent.",
                "newCardNumber");
        }

        if (!clearCurrentCard && newCardNumberRaw is null)
        {
            return ClientCommandSupport.ValidationError(
                "New card number is required unless clear-card intent is explicit.",
                "newCardNumber");
        }

        if (newCardNumberRaw is null)
        {
            return null;
        }

        try
        {
            newCardNumberNormalized = ClientSearchNormalizer.NormalizeCardNumber(newCardNumberRaw);
            return null;
        }
        catch (ArgumentException exception)
        {
            return ClientCommandSupport.ValidationError(exception.Message, "newCardNumber");
        }
    }

    private static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == CurrentCardUniqueConstraint)
        {
            result = ClientCommandSupport.Error(
                CommandErrorCode.CardNumberAlreadyCurrent,
                "Card number became current for another client. Refresh and try again.",
                "newCardNumber");
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == CurrentClientUniqueConstraint)
        {
            result = ClientCommandSupport.Error(
                CommandErrorCode.ConcurrencyConflict,
                "Client card assignment changed concurrently. Refresh canonical state and try again.");
            return true;
        }

        return ClientCommandSupport.TryMapCommonPostgresFailure(exception, out result);
    }

    private sealed record CardAssignmentSnapshot(
        Guid Id,
        string CardNumber,
        string CardNumberNormalized,
        DateTimeOffset AssignedAt)
    {
        internal static CardAssignmentSnapshot? From(ClientCardAssignmentRecord? assignment)
        {
            return assignment is null
                ? null
                : new CardAssignmentSnapshot(
                    assignment.Id,
                    assignment.CardNumberRaw,
                    assignment.CardNumberNormalized,
                    assignment.AssignedAt);
        }
    }
}
