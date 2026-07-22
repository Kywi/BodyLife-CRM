using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class UpdateClientCommandHandler(
    BodyLifeDbContext dbContext,
    IBodyLifeQueryHandler<FindClientDuplicateCandidatesQuery, IReadOnlyList<ClientDuplicateCandidate>>
        duplicateCandidateQueryHandler,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<UpdateClientCommand>
{
    private const string CommandName = "UpdateClient";

    public async Task<CommandResult> ExecuteAsync(
        UpdateClientCommand command,
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

        if (command.ExpectedUpdatedAt == default)
        {
            return ClientCommandSupport.ValidationError(
                "Expected updated_at is required.",
                "expectedUpdatedAt");
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

        var identity = normalizedIdentity!;
        var canonicalEnvelope = identity.CanonicalEnvelope;
        var expectedUpdatedAt = command.ExpectedUpdatedAt.ToUniversalTime();
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = ClientCommandSupport.CreateUpdateClientFingerprint(
            canonicalEnvelope,
            identity,
            command.ClientId,
            expectedUpdatedAt);
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

            var client = await dbContext.Set<ClientRecord>()
                .SingleOrDefaultAsync(record => record.Id == command.ClientId, cancellationToken);

            if (client is null)
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId");
            }

            if (client.UpdatedAt != expectedUpdatedAt)
            {
                return ClientCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Client changed after the edit form was loaded. Refresh canonical state.",
                    "expectedUpdatedAt");
            }

            var duplicateCandidates = await duplicateCandidateQueryHandler.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    identity.Surname,
                    identity.Name,
                    identity.Patronymic,
                    identity.PhoneRaw,
                    ExcludedClientId: client.Id),
                cancellationToken);
            var acknowledgementValidation = ClientCommandSupport.ValidateAcknowledgements(
                duplicateCandidates,
                identity.Acknowledgements);

            if (acknowledgementValidation is not null)
            {
                return acknowledgementValidation;
            }

            var before = ClientIdentitySnapshot.From(client);
            var hasIdentityChanges = !before.Matches(identity);

            if (!hasIdentityChanges && identity.Acknowledgements.Count == 0)
            {
                return ClientCommandSupport.ValidationError(
                    "At least one client field must change.",
                    field: null);
            }

            client.Surname = identity.Surname;
            client.Name = identity.Name;
            client.Patronymic = identity.Patronymic;
            client.NormalizedFullName = identity.NormalizedFullName;
            client.PhoneRaw = identity.PhoneRaw;
            client.PhoneNormalized = identity.PhoneNormalized;
            client.PhoneLastFour = identity.PhoneLastFour;
            client.Comment = identity.Comment;
            client.OperationalStatus = identity.OperationalStatus;
            client.UpdatedAt = NextUpdatedAt(client.UpdatedAt, recordedAt);

            var acknowledgementRecords = identity.Acknowledgements
                .Select(acknowledgement => new DuplicateWarningAcknowledgementRecord
                {
                    Id = Guid.NewGuid(),
                    ClientId = client.Id,
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
                ClientAuditActions.Updated,
                ClientAuditActions.EntityType,
                client.Id,
                recordedAt,
                relatedEntityRefs: new
                {
                    DuplicateWarningAcknowledgementIds = acknowledgementRecords
                        .Select(record => record.Id)
                        .ToArray(),
                    MatchedClientIds = acknowledgementRecords
                        .Select(record => record.MatchedClientId)
                        .Distinct()
                        .ToArray(),
                },
                beforeSummary: before,
                afterSummary: new
                {
                    client.Surname,
                    client.Name,
                    client.Patronymic,
                    Phone = client.PhoneRaw,
                    client.OperationalStatus,
                    client.Comment,
                    client.UpdatedAt,
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
                || !ClientCommandSupport.TryMapCommonPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                throw;
            }

            await ClientCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            return errorResult;
        }
    }

    private static DateTimeOffset NextUpdatedAt(
        DateTimeOffset previousUpdatedAt,
        DateTimeOffset recordedAt)
    {
        return recordedAt > previousUpdatedAt
            ? recordedAt
            : previousUpdatedAt.AddTicks(10);
    }

    private sealed record ClientIdentitySnapshot(
        string Surname,
        string Name,
        string? Patronymic,
        string? Phone,
        string OperationalStatus,
        string? Comment,
        DateTimeOffset UpdatedAt)
    {
        internal static ClientIdentitySnapshot From(ClientRecord client)
        {
            return new ClientIdentitySnapshot(
                client.Surname,
                client.Name,
                client.Patronymic,
                client.PhoneRaw,
                client.OperationalStatus,
                client.Comment,
                client.UpdatedAt);
        }

        internal bool Matches(NormalizedClientIdentity identity)
        {
            return Surname == identity.Surname
                && Name == identity.Name
                && Patronymic == identity.Patronymic
                && Phone == identity.PhoneRaw
                && OperationalStatus == identity.OperationalStatus
                && Comment == identity.Comment;
        }
    }
}
