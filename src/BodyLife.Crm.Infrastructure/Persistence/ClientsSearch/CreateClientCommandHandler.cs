using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private const string ClientEntityType = "client";
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string CurrentCardUniqueConstraint = "ux_client_card_assignments_current_card";
    private const string IdempotencyUniqueConstraint = "ux_command_idempotency_keys_command_key";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 2000;
    private const int AcknowledgementReasonMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    public async Task<CommandResult> ExecuteAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null || !IsAllowedActorShape(command.Envelope.Actor))
        {
            return Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner, named Admin or shared Reception/Admin session is required.");
        }

        var validationResult = ValidateAndNormalize(command, out var normalizedInput);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var input = normalizedInput!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = CreateFingerprint(command.Envelope, input);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (!await IsCanonicalActorAuthorizedAsync(command.Envelope.Actor, recordedAt, cancellationToken))
            {
                return Error(
                    CommandErrorCode.PermissionDenied,
                    "The actor account or session is not active.");
            }

            var existingIdempotency = await dbContext.Set<CommandIdempotencyRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    record => record.CommandName == CommandName
                        && record.IdempotencyKey == input.IdempotencyKey,
                    cancellationToken);

            if (existingIdempotency is not null)
            {
                return ReplayOrRejectDuplicate(
                    existingIdempotency,
                    command.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            if (input.CardNumberNormalized is not null
                && await dbContext.Set<ClientCardAssignmentRecord>()
                    .AsNoTracking()
                    .AnyAsync(
                        assignment => assignment.IsCurrent
                            && assignment.CardNumberNormalized == input.CardNumberNormalized,
                        cancellationToken))
            {
                return Error(
                    CommandErrorCode.CardNumberAlreadyCurrent,
                    "Card number is already assigned to a current client.",
                    "cardNumber");
            }

            var duplicateCandidates = await duplicateCandidateQueryHandler.ExecuteAsync(
                new FindClientDuplicateCandidatesQuery(
                    input.Surname,
                    input.Name,
                    input.Patronymic,
                    input.PhoneRaw),
                cancellationToken);
            var acknowledgementValidation = ValidateAcknowledgements(
                duplicateCandidates,
                input.Acknowledgements);

            if (acknowledgementValidation is not null)
            {
                return acknowledgementValidation;
            }

            var clientId = Guid.NewGuid();
            var client = new ClientRecord
            {
                Id = clientId,
                Surname = input.Surname,
                Name = input.Name,
                Patronymic = input.Patronymic,
                NormalizedFullName = input.NormalizedFullName,
                PhoneRaw = input.PhoneRaw,
                PhoneNormalized = input.PhoneNormalized,
                PhoneLastFour = input.PhoneLastFour,
                Comment = input.Comment,
                OperationalStatus = input.OperationalStatus,
                CreatedAt = recordedAt,
                CreatedByAccountId = command.Envelope.Actor.AccountId.Value,
                UpdatedAt = recordedAt,
            };
            dbContext.Set<ClientRecord>().Add(client);

            var cardAssignment = CreateCardAssignment(command, input, clientId, recordedAt);

            if (cardAssignment is not null)
            {
                dbContext.Set<ClientCardAssignmentRecord>().Add(cardAssignment);
            }

            var acknowledgementRecords = input.Acknowledgements
                .Select(acknowledgement => new DuplicateWarningAcknowledgementRecord
                {
                    Id = Guid.NewGuid(),
                    ClientId = clientId,
                    WarningType = MapWarningType(acknowledgement.WarningType),
                    MatchedClientId = acknowledgement.MatchedClientId,
                    AcknowledgedByAccountId = command.Envelope.Actor.AccountId.Value,
                    AcknowledgedAt = recordedAt,
                    Reason = acknowledgement.Reason,
                })
                .ToArray();
            dbContext.Set<DuplicateWarningAcknowledgementRecord>().AddRange(acknowledgementRecords);

            var auditEntryId = auditAppender.Append(
                command.Envelope,
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

            dbContext.Set<CommandIdempotencyRecord>().Add(new CommandIdempotencyRecord
            {
                Id = Guid.NewGuid(),
                CommandName = CommandName,
                IdempotencyKey = input.IdempotencyKey,
                RequestCorrelationId = input.RequestCorrelationId,
                AccountId = command.Envelope.Actor.AccountId.Value,
                ActorRole = MapActorRole(command.Envelope.Actor.Role),
                AccountKind = MapAccountKind(command.Envelope.Actor.AccountKind),
                SessionId = command.Envelope.Actor.SessionId.Value,
                DeviceLabel = input.DeviceLabel,
                EntryOrigin = MapEntryOrigin(command.Envelope.EntryOrigin),
                Status = SucceededIdempotencyStatus,
                CreatedAt = recordedAt,
                CompletedAt = recordedAt,
                ExpiresAt = recordedAt.Add(IdempotencyRetention),
                PrimaryEntityId = clientId,
                RereadTargetId = clientId,
                AuditEntryId = auditEntryId.Value,
                ResultFingerprint = fingerprint,
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Success(clientId, auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = FindPostgresException(exception);

            if (postgresException is null
                || !TryMapPostgresFailure(postgresException, out var errorResult))
            {
                throw;
            }

            await RollBackAndClearAsync(transaction);
            return errorResult;
        }
    }

    private async Task<bool> IsCanonicalActorAuthorizedAsync(
        ActorContext actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accountType = MapAccountKind(actor.AccountKind);
        var role = MapActorRole(actor.Role);
        var accountIsActive = await dbContext.Set<AccountRecord>()
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == actor.AccountId.Value
                    && account.IsActive
                    && account.AccountType == accountType
                    && account.Role == role,
                cancellationToken);

        if (!accountIsActive)
        {
            return false;
        }

        return await dbContext.Set<SessionRecord>()
            .AsNoTracking()
            .AnyAsync(
                session => session.Id == actor.SessionId.Value
                    && session.AccountId == actor.AccountId.Value
                    && session.EndedAt == null
                    && session.ExpiresAt > now,
                cancellationToken);
    }

    private static CommandResult? ValidateAndNormalize(
        CreateClientCommand command,
        out NormalizedCreateClient? normalizedInput)
    {
        normalizedInput = null;
        var envelope = command.Envelope;
        var idempotencyKey = envelope.IdempotencyKey?.Trim();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return ValidationError("Idempotency key is required.", "idempotencyKey");
        }

        if (idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ValidationError(
                $"Idempotency key must be {IdempotencyKeyMaxLength} characters or fewer.",
                "idempotencyKey");
        }

        var requestCorrelationId = envelope.RequestCorrelationId.Value?.Trim();

        if (string.IsNullOrWhiteSpace(requestCorrelationId)
            || requestCorrelationId.Length > CorrelationIdMaxLength)
        {
            return ValidationError(
                $"Request correlation id is required and must be {CorrelationIdMaxLength} characters or fewer.",
                "requestCorrelationId");
        }

        var deviceLabel = NormalizeOptional(envelope.Actor.DeviceLabel);

        if (deviceLabel?.Length > DeviceLabelMaxLength)
        {
            return ValidationError(
                $"Device label must be {DeviceLabelMaxLength} characters or fewer.",
                "deviceLabel");
        }

        if (!Enum.IsDefined(envelope.EntryOrigin))
        {
            return ValidationError("Entry origin is invalid.", "entryOrigin");
        }

        var envelopeReason = NormalizeOptional(envelope.Reason);
        var envelopeComment = NormalizeOptional(envelope.Comment);

        if (envelopeReason?.Length > AuditReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {AuditReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (envelopeComment?.Length > AuditCommentMaxLength)
        {
            return ValidationError(
                $"Envelope comment must be {AuditCommentMaxLength} characters or fewer.",
                "envelope.comment");
        }

        if (envelope.EntryOrigin != EntryOrigin.Normal
            && (envelope.OccurredAt is null
                || (envelopeReason is null && envelopeComment is null)))
        {
            return ValidationError(
                "Non-normal client creation requires occurred_at and a reason or command comment.",
                "entryOrigin");
        }

        if (!Enum.IsDefined(command.OperationalStatus))
        {
            return ValidationError("Operational status is invalid.", "operationalStatus");
        }

        string normalizedFullName;
        string? phoneRaw;
        string? phoneNormalized;
        string? phoneLastFour;
        string? cardNumberRaw;
        string? cardNumberNormalized;

        try
        {
            normalizedFullName = ClientSearchNormalizer.NormalizeFullName(
                command.Surname,
                command.Name,
                command.Patronymic);
            phoneRaw = NormalizeOptional(command.Phone);
            phoneNormalized = phoneRaw is null
                ? null
                : ClientSearchNormalizer.NormalizePhone(phoneRaw);
            phoneLastFour = phoneNormalized is null
                ? null
                : ClientSearchNormalizer.ExtractPhoneLastFour(phoneNormalized);
            cardNumberRaw = NormalizeOptional(command.CardNumber);
            cardNumberNormalized = cardNumberRaw is null
                ? null
                : ClientSearchNormalizer.NormalizeCardNumber(cardNumberRaw);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, exception.ParamName);
        }

        if (command.DuplicateWarningAcknowledgements is null)
        {
            return ValidationError(
                "Duplicate warning acknowledgements collection is required.",
                "duplicateWarningAcknowledgements");
        }

        var acknowledgementKeys = new HashSet<(Guid MatchedClientId, ClientDuplicateWarningType WarningType)>();
        var acknowledgements = new List<NormalizedAcknowledgement>(
            command.DuplicateWarningAcknowledgements.Count);

        foreach (var acknowledgement in command.DuplicateWarningAcknowledgements)
        {
            if (acknowledgement is null
                || acknowledgement.MatchedClientId == Guid.Empty
                || !Enum.IsDefined(acknowledgement.WarningType))
            {
                return ValidationError(
                    "Duplicate warning acknowledgement identity is invalid.",
                    "duplicateWarningAcknowledgements");
            }

            var reason = acknowledgement.Reason?.Trim();

            if (string.IsNullOrWhiteSpace(reason)
                || reason.Length > AcknowledgementReasonMaxLength)
            {
                return ValidationError(
                    $"Acknowledgement reason is required and must be {AcknowledgementReasonMaxLength} characters or fewer.",
                    "duplicateWarningAcknowledgements.reason");
            }

            if (!acknowledgementKeys.Add((acknowledgement.MatchedClientId, acknowledgement.WarningType)))
            {
                return ValidationError(
                    "Duplicate warning acknowledgement was supplied more than once.",
                    "duplicateWarningAcknowledgements");
            }

            acknowledgements.Add(new NormalizedAcknowledgement(
                acknowledgement.MatchedClientId,
                acknowledgement.WarningType,
                reason));
        }

        normalizedInput = new NormalizedCreateClient(
            command.Surname.Trim(),
            command.Name.Trim(),
            NormalizeOptional(command.Patronymic),
            normalizedFullName,
            phoneRaw,
            phoneNormalized,
            phoneLastFour,
            cardNumberRaw,
            cardNumberNormalized,
            NormalizeOptional(command.Comment),
            MapOperationalStatus(command.OperationalStatus),
            acknowledgements
                .OrderBy(acknowledgement => acknowledgement.WarningType)
                .ThenBy(acknowledgement => acknowledgement.MatchedClientId)
                .ToArray(),
            idempotencyKey,
            requestCorrelationId,
            deviceLabel);
        return null;
    }

    private static CommandResult? ValidateAcknowledgements(
        IReadOnlyList<ClientDuplicateCandidate> candidates,
        IReadOnlyList<NormalizedAcknowledgement> acknowledgements)
    {
        var candidateKeys = candidates
            .Select(candidate => (candidate.MatchedClientId, candidate.WarningType))
            .ToHashSet();
        var acknowledgementKeys = acknowledgements
            .Select(acknowledgement => (acknowledgement.MatchedClientId, acknowledgement.WarningType))
            .ToHashSet();
        var unexpectedAcknowledgement = acknowledgementKeys
            .FirstOrDefault(key => !candidateKeys.Contains(key));

        if (unexpectedAcknowledgement != default)
        {
            return ValidationError(
                "An acknowledgement does not match a current duplicate candidate. Refresh duplicate warnings.",
                "duplicateWarningAcknowledgements");
        }

        var missingCandidates = candidateKeys
            .Where(candidate => !acknowledgementKeys.Contains(candidate))
            .OrderBy(candidate => candidate.WarningType)
            .ThenBy(candidate => candidate.MatchedClientId)
            .ToArray();

        if (missingCandidates.Length == 0)
        {
            return null;
        }

        return CommandResult.Error(missingCandidates
            .Select(candidate => new CommandError(
                CommandErrorCode.DuplicateWarningNotAcknowledged,
                $"{MapWarningType(candidate.WarningType)} warning for matched client {candidate.MatchedClientId} must be acknowledged.",
                "duplicateWarningAcknowledgements"))
            .ToArray());
    }

    private static ClientCardAssignmentRecord? CreateCardAssignment(
        CreateClientCommand command,
        NormalizedCreateClient input,
        Guid clientId,
        DateTimeOffset recordedAt)
    {
        return input.CardNumberNormalized is null
            ? null
            : new ClientCardAssignmentRecord
            {
                Id = Guid.NewGuid(),
                ClientId = clientId,
                CardNumberRaw = input.CardNumberRaw!,
                CardNumberNormalized = input.CardNumberNormalized,
                AssignedAt = command.Envelope.OccurredAt ?? recordedAt,
                AssignedByAccountId = command.Envelope.Actor.AccountId.Value,
                EndedAt = null,
                EndedByAccountId = null,
                EndReason = null,
                IsCurrent = true,
            };
    }

    private static string CreateFingerprint(
        CommandEnvelope envelope,
        NormalizedCreateClient input)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = NormalizeOptional(envelope.Reason),
            EnvelopeComment = NormalizeOptional(envelope.Comment),
            input.NormalizedFullName,
            input.PhoneNormalized,
            input.CardNumberNormalized,
            input.Comment,
            input.OperationalStatus,
            Acknowledgements = input.Acknowledgements.Select(acknowledgement => new
            {
                acknowledgement.MatchedClientId,
                WarningType = MapWarningType(acknowledgement.WarningType),
                acknowledgement.Reason,
            }),
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    private static CommandResult ReplayOrRejectDuplicate(
        CommandIdempotencyRecord record,
        Guid actorAccountId,
        string fingerprint)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == actorAccountId
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId.HasValue
            && record.RereadTargetId == record.PrimaryEntityId
            && record.AuditEntryId.HasValue)
        {
            return Success(
                record.PrimaryEntityId.Value,
                new AuditEntryId(record.AuditEntryId.Value));
        }

        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete CreateClient request.",
            "idempotencyKey");
    }

    private static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == CurrentCardUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.CardNumberAlreadyCurrent,
                "Card number became current for another client. Refresh and try again.",
                "cardNumber");
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.DuplicateSubmission,
                "CreateClient request with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Client identity changed concurrently. Refresh canonical state and try again.");
            return true;
        }

        result = null!;
        return false;
    }

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
            {
                return postgresException;
            }
        }

        return null;
    }

    private async Task RollBackAndClearAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    private static bool IsAllowedActorShape(ActorContext actor)
    {
        return actor.AccountId.Value != Guid.Empty
            && actor.SessionId.Value != Guid.Empty
            && actor switch
            {
                { Role: ActorRole.Owner, AccountKind: AccountKind.Owner } => true,
                {
                    Role: ActorRole.Admin,
                    AccountKind: AccountKind.NamedAdmin or AccountKind.SharedReceptionAdmin,
                } => true,
                _ => false,
            };
    }

    private static string MapOperationalStatus(ClientOperationalStatus status)
    {
        return status switch
        {
            ClientOperationalStatus.Active => "active",
            ClientOperationalStatus.Inactive => "inactive",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    private static string MapWarningType(ClientDuplicateWarningType warningType)
    {
        return warningType switch
        {
            ClientDuplicateWarningType.DuplicatePhone => "duplicate_phone",
            ClientDuplicateWarningType.SimilarName => "similar_name",
            _ => throw new ArgumentOutOfRangeException(nameof(warningType), warningType, null),
        };
    }

    private static string MapAccountKind(AccountKind accountKind)
    {
        return accountKind switch
        {
            AccountKind.Owner => "owner",
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapActorRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => "owner",
            ActorRole.Admin => "admin",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static string MapEntryOrigin(EntryOrigin entryOrigin)
    {
        return entryOrigin switch
        {
            EntryOrigin.Normal => "normal",
            EntryOrigin.ManualBackfill => "manual_backfill",
            EntryOrigin.PaperFallback => "paper_fallback",
            EntryOrigin.FutureImport => "future_import",
            _ => throw new ArgumentOutOfRangeException(nameof(entryOrigin), entryOrigin, null),
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static CommandResult Success(Guid clientId, AuditEntryId auditEntryId)
    {
        var entityId = new EntityId(ClientEntityType, clientId);
        return CommandResult.Success(entityId, entityId, auditEntryId: auditEntryId);
    }

    private static CommandResult ValidationError(string message, string? field)
    {
        return Error(CommandErrorCode.ValidationFailed, message, field);
    }

    private static CommandResult Error(
        CommandErrorCode code,
        string message,
        string? field = null)
    {
        return CommandResult.Error([new CommandError(code, message, field)]);
    }

    private sealed record NormalizedCreateClient(
        string Surname,
        string Name,
        string? Patronymic,
        string NormalizedFullName,
        string? PhoneRaw,
        string? PhoneNormalized,
        string? PhoneLastFour,
        string? CardNumberRaw,
        string? CardNumberNormalized,
        string? Comment,
        string OperationalStatus,
        IReadOnlyList<NormalizedAcknowledgement> Acknowledgements,
        string IdempotencyKey,
        string RequestCorrelationId,
        string? DeviceLabel);

    private sealed record NormalizedAcknowledgement(
        Guid MatchedClientId,
        ClientDuplicateWarningType WarningType,
        string Reason);
}
