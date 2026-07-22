using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class CreateClientCommandHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<FindClientDuplicateCandidatesQuery, IReadOnlyList<ClientDuplicateCandidate>>
        duplicateCandidateQueryHandler,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreateClientCommand>
{
    private const string CommandName = "CreateClient";
    private const string CurrentCardUniqueConstraint = "ux_client_card_assignments_current_card";

    public async Task<CommandResult> ExecuteAsync(
        CreateClientCommand command,
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

        var validationResult = ClientCommandSupport.ValidateAndNormalizeIdentity(
            command.Envelope,
            command.Surname,
            command.Name,
            command.Patronymic,
            command.Phone,
            command.Comment,
            command.OperationalStatus,
            command.DuplicateWarningAcknowledgements,
            out var normalizedIdentity);

        if (validationResult is not null)
        {
            return validationResult;
        }

        string? cardNumberRaw;
        string? cardNumberNormalized;

        try
        {
            cardNumberRaw = ClientCommandSupport.NormalizeOptional(command.CardNumber);
            cardNumberNormalized = cardNumberRaw is null
                ? null
                : ClientSearchNormalizer.NormalizeCardNumber(cardNumberRaw);
        }
        catch (ArgumentException exception)
        {
            return ClientCommandSupport.ValidationError(exception.Message, exception.ParamName);
        }

        var identity = normalizedIdentity!;
        var canonicalEnvelope = identity.CanonicalEnvelope;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = ClientCommandSupport.CreateClientFingerprint(
            canonicalEnvelope,
            identity,
            cardNumberNormalized);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (!await ClientCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    canonicalEnvelope.Actor,
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
                identity.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return ClientCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    canonicalEnvelope.Actor.AccountId.Value,
                    fingerprint);
            }

            if (cardNumberNormalized is not null
                && await dbContext.Set<ClientCardAssignmentRecord>()
                    .AsNoTracking()
                    .AnyAsync(
                        assignment => assignment.IsCurrent
                            && assignment.CardNumberNormalized == cardNumberNormalized,
                        cancellationToken))
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.CardNumberAlreadyCurrent,
                    "Card number is already assigned to a current client.",
                    "cardNumber");
            }

            var duplicateCandidates = await duplicateCandidateQueryHandler.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    identity.Surname,
                    identity.Name,
                    identity.Patronymic,
                    identity.PhoneRaw),
                cancellationToken);
            var acknowledgementValidation = ClientCommandSupport.ValidateAcknowledgements(
                duplicateCandidates,
                identity.Acknowledgements);

            if (acknowledgementValidation is not null)
            {
                return acknowledgementValidation;
            }

            var clientId = Guid.NewGuid();
            var client = new ClientRecord
            {
                Id = clientId,
                Surname = identity.Surname,
                Name = identity.Name,
                Patronymic = identity.Patronymic,
                NormalizedFullName = identity.NormalizedFullName,
                PhoneRaw = identity.PhoneRaw,
                PhoneNormalized = identity.PhoneNormalized,
                PhoneLastFour = identity.PhoneLastFour,
                Comment = identity.Comment,
                OperationalStatus = identity.OperationalStatus,
                CreatedAt = recordedAt,
                CreatedByAccountId = canonicalEnvelope.Actor.AccountId.Value,
                UpdatedAt = recordedAt,
            };
            dbContext.Set<ClientRecord>().Add(client);

            var cardAssignment = cardNumberNormalized is null
                ? null
                : new ClientCardAssignmentRecord
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    CardNumberRaw = cardNumberRaw!,
                    CardNumberNormalized = cardNumberNormalized,
                    AssignedAt = canonicalEnvelope.OccurredAt ?? recordedAt,
                    AssignedByAccountId = canonicalEnvelope.Actor.AccountId.Value,
                    EndedAt = null,
                    EndedByAccountId = null,
                    EndReason = null,
                    IsCurrent = true,
                };

            if (cardAssignment is not null)
            {
                dbContext.Set<ClientCardAssignmentRecord>().Add(cardAssignment);
            }

            var acknowledgementRecords = identity.Acknowledgements
                .Select(acknowledgement => new DuplicateWarningAcknowledgementRecord
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    WarningType = ClientCommandSupport.MapWarningType(acknowledgement.WarningType),
                    MatchedClientId = acknowledgement.MatchedClientId,
                    AcknowledgedByAccountId = canonicalEnvelope.Actor.AccountId.Value,
                    AcknowledgedAt = recordedAt,
                    Reason = acknowledgement.Reason,
                })
                .ToArray();
            dbContext.Set<DuplicateWarningAcknowledgementRecord>().AddRange(acknowledgementRecords);

            var auditEntryId = auditAppender.Append(
                canonicalEnvelope,
                ClientAuditActions.Created,
                ClientAuditActions.EntityType,
                clientId,
                recordedAt,
                relatedEntityRefs: new
                {
                    CardAssignmentId = cardAssignment?.Id,
                    DuplicateWarningAcknowledgementIds = acknowledgementRecords
                        .Select(record => record.Id)
                        .ToArray(),
                    MatchedClientIds = acknowledgementRecords
                        .Select(record => record.MatchedClientId)
                        .Distinct()
                        .ToArray(),
                },
                afterSummary: new
                {
                    client.Surname,
                    client.Name,
                    client.Patronymic,
                    Phone = client.PhoneRaw,
                    client.OperationalStatus,
                    client.Comment,
                    CardNumber = cardAssignment?.CardNumberRaw,
                    DuplicateWarningAcknowledgements = acknowledgementRecords.Select(record => new
                    {
                        record.WarningType,
                        record.MatchedClientId,
                        record.Reason,
                    }),
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                ClientCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    canonicalEnvelope,
                    identity,
                    recordedAt,
                    clientId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return ClientCommandSupport.Success(clientId, auditEntryId);
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
                "cardNumber");
            return true;
        }

        return ClientCommandSupport.TryMapCommonPostgresFailure(exception, out result);
    }
}
