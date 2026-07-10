using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal static class ClientCommandSupport
{
    private const string ClientEntityType = "client";
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint = "ux_command_idempotency_keys_command_key";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 2000;
    private const int AcknowledgementReasonMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static bool IsAllowedActorShape(ActorContext actor)
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

    internal static async Task<bool> IsCanonicalActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
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

    internal static CommandResult? ValidateAndNormalizeIdentity(
        CommandEnvelope envelope,
        string? surname,
        string? name,
        string? patronymic,
        string? phone,
        string? comment,
        ClientOperationalStatus operationalStatus,
        IReadOnlyList<ClientDuplicateWarningAcknowledgement>? duplicateWarningAcknowledgements,
        out NormalizedClientIdentity? normalizedIdentity)
    {
        normalizedIdentity = null;
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
                "Non-normal client mutation requires occurred_at and a reason or command comment.",
                "entryOrigin");
        }

        if (!Enum.IsDefined(operationalStatus))
        {
            return ValidationError("Operational status is invalid.", "operationalStatus");
        }

        string normalizedFullName;
        string? phoneRaw;
        string? phoneNormalized;
        string? phoneLastFour;

        try
        {
            normalizedFullName = ClientSearchNormalizer.NormalizeFullName(surname, name, patronymic);
            phoneRaw = NormalizeOptional(phone);
            phoneNormalized = phoneRaw is null
                ? null
                : ClientSearchNormalizer.NormalizePhone(phoneRaw);
            phoneLastFour = phoneNormalized is null
                ? null
                : ClientSearchNormalizer.ExtractPhoneLastFour(phoneNormalized);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, exception.ParamName);
        }

        if (duplicateWarningAcknowledgements is null)
        {
            return ValidationError(
                "Duplicate warning acknowledgements collection is required.",
                "duplicateWarningAcknowledgements");
        }

        var acknowledgementKeys = new HashSet<(Guid MatchedClientId, ClientDuplicateWarningType WarningType)>();
        var acknowledgements = new List<NormalizedClientAcknowledgement>(
            duplicateWarningAcknowledgements.Count);

        foreach (var acknowledgement in duplicateWarningAcknowledgements)
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

            acknowledgements.Add(new NormalizedClientAcknowledgement(
                acknowledgement.MatchedClientId,
                acknowledgement.WarningType,
                reason));
        }

        normalizedIdentity = new NormalizedClientIdentity(
            surname!.Trim(),
            name!.Trim(),
            NormalizeOptional(patronymic),
            normalizedFullName,
            phoneRaw,
            phoneNormalized,
            phoneLastFour,
            NormalizeOptional(comment),
            MapOperationalStatus(operationalStatus),
            acknowledgements
                .OrderBy(acknowledgement => acknowledgement.WarningType)
                .ThenBy(acknowledgement => acknowledgement.MatchedClientId)
                .ToArray(),
            idempotencyKey,
            requestCorrelationId,
            deviceLabel);
        return null;
    }

    internal static CommandResult? ValidateAcknowledgements(
        IReadOnlyList<ClientDuplicateCandidate> candidates,
        IReadOnlyList<NormalizedClientAcknowledgement> acknowledgements)
    {
        var candidateKeys = candidates
            .Select(candidate => (candidate.MatchedClientId, candidate.WarningType))
            .ToHashSet();
        var acknowledgementKeys = acknowledgements
            .Select(acknowledgement => (acknowledgement.MatchedClientId, acknowledgement.WarningType))
            .ToHashSet();

        if (!acknowledgementKeys.IsSubsetOf(candidateKeys))
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

    internal static string CreateClientFingerprint(
        CommandEnvelope envelope,
        NormalizedClientIdentity identity,
        string? cardNumberNormalized)
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
            identity.NormalizedFullName,
            identity.PhoneNormalized,
            CardNumberNormalized = cardNumberNormalized,
            identity.Comment,
            identity.OperationalStatus,
            Acknowledgements = identity.Acknowledgements.Select(acknowledgement => new
            {
                acknowledgement.MatchedClientId,
                WarningType = MapWarningType(acknowledgement.WarningType),
                acknowledgement.Reason,
            }),
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static string CreateUpdateClientFingerprint(
        CommandEnvelope envelope,
        NormalizedClientIdentity identity,
        Guid clientId,
        DateTimeOffset expectedUpdatedAt)
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
            identity.NormalizedFullName,
            identity.PhoneNormalized,
            identity.Comment,
            identity.OperationalStatus,
            Acknowledgements = identity.Acknowledgements.Select(acknowledgement => new
            {
                acknowledgement.MatchedClientId,
                WarningType = MapWarningType(acknowledgement.WarningType),
                acknowledgement.Reason,
            }),
            Command = new
            {
                ClientId = clientId,
                ExpectedUpdatedAt = expectedUpdatedAt,
            },
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static Task<CommandIdempotencyRecord?> FindIdempotencyAsync(
        BodyLifeDbContext dbContext,
        string commandName,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<CommandIdempotencyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.CommandName == commandName
                    && record.IdempotencyKey == idempotencyKey,
                cancellationToken);
    }

    internal static CommandResult ReplayOrRejectDuplicate(
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
            "Idempotency key has already been used by a different or incomplete client command request.",
            "idempotencyKey");
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        CommandEnvelope envelope,
        NormalizedClientIdentity identity,
        DateTimeOffset recordedAt,
        Guid clientId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = identity.IdempotencyKey,
            RequestCorrelationId = identity.RequestCorrelationId,
            AccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            AccountKind = MapAccountKind(envelope.Actor.AccountKind),
            SessionId = envelope.Actor.SessionId.Value,
            DeviceLabel = identity.DeviceLabel,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = clientId,
            RereadTargetId = clientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static bool TryMapCommonPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.DuplicateSubmission,
                "Client command with this idempotency key is already in progress or completed.",
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

    internal static PostgresException? FindPostgresException(Exception exception)
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

    internal static async Task RollBackAndClearAsync(
        BodyLifeDbContext dbContext,
        IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    internal static string MapWarningType(ClientDuplicateWarningType warningType)
    {
        return warningType switch
        {
            ClientDuplicateWarningType.DuplicatePhone => "duplicate_phone",
            ClientDuplicateWarningType.SimilarName => "similar_name",
            _ => throw new ArgumentOutOfRangeException(nameof(warningType), warningType, null),
        };
    }

    internal static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    internal static CommandResult Success(Guid clientId, AuditEntryId auditEntryId)
    {
        var entityId = new EntityId(ClientEntityType, clientId);
        return CommandResult.Success(entityId, entityId, auditEntryId: auditEntryId);
    }

    internal static CommandResult ValidationError(string message, string? field)
    {
        return Error(CommandErrorCode.ValidationFailed, message, field);
    }

    internal static CommandResult Error(
        CommandErrorCode code,
        string message,
        string? field = null)
    {
        return CommandResult.Error([new CommandError(code, message, field)]);
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
}

internal sealed record NormalizedClientIdentity(
    string Surname,
    string Name,
    string? Patronymic,
    string NormalizedFullName,
    string? PhoneRaw,
    string? PhoneNormalized,
    string? PhoneLastFour,
    string? Comment,
    string OperationalStatus,
    IReadOnlyList<NormalizedClientAcknowledgement> Acknowledgements,
    string IdempotencyKey,
    string RequestCorrelationId,
    string? DeviceLabel);

internal sealed record NormalizedClientAcknowledgement(
    Guid MatchedClientId,
    ClientDuplicateWarningType WarningType,
    string Reason);
