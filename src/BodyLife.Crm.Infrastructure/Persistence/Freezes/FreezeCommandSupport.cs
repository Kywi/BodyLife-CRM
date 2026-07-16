using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Freezes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Freezes;

internal static class FreezeCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const string FreezeCancellationUniqueConstraint =
        "ux_freeze_cancellations_freeze_id";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static bool IsAllowedActorShape(ActorContext? actor)
    {
        return actor is not null
            && actor.AccountId.Value != Guid.Empty
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

    internal static CommandResult? ValidateAndNormalize(
        AddFreezeCommand command,
        out NormalizedAddFreeze? normalizedFreeze)
    {
        normalizedFreeze = null;

        if (command.ClientId == Guid.Empty)
        {
            return ValidationError("Client id is required.", "clientId");
        }

        if (command.MembershipId == Guid.Empty)
        {
            return ValidationError("Membership id is required.", "membershipId");
        }

        if (command.Range.StartDate == default || command.Range.EndDate == default)
        {
            return ValidationError("Freeze range dates are required.", "range");
        }

        if (command.EntryBatchId == Guid.Empty)
        {
            return ValidationError(
                "Entry batch id must be a non-empty identifier when supplied.",
                "entryBatchId");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var canonicalEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (canonicalEnvelope!.EntryOrigin == EntryOrigin.Normal
            && command.EntryBatchId is not null)
        {
            return ValidationError(
                "Normal Freeze entry cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        normalizedFreeze = new NormalizedAddFreeze(
            command.ClientId,
            command.MembershipId,
            command.Range,
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static CommandResult? ValidateAndNormalize(
        CancelFreezeCommand command,
        out NormalizedCancelFreeze? normalizedCancellation)
    {
        normalizedCancellation = null;

        if (command.FreezeId == Guid.Empty)
        {
            return ValidationError("Freeze id is required.", "freezeId");
        }

        if (command.EntryBatchId == Guid.Empty)
        {
            return ValidationError(
                "Entry batch id must be a non-empty identifier when supplied.",
                "entryBatchId");
        }

        var envelopeValidation = ValidateAndNormalizeCancellationEnvelope(
            command.Envelope,
            out var canonicalEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (canonicalEnvelope!.EntryOrigin == EntryOrigin.Normal
            && command.EntryBatchId is not null)
        {
            return ValidationError(
                "Normal Freeze cancellation cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        normalizedCancellation = new NormalizedCancelFreeze(
            command.FreezeId,
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static string CreateFingerprint(NormalizedAddFreeze freeze)
    {
        var envelope = freeze.Envelope;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = envelope.Reason,
            EnvelopeComment = envelope.Comment,
            freeze.ClientId,
            freeze.MembershipId,
            freeze.Range.StartDate,
            freeze.Range.EndDate,
            freeze.EntryBatchId,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static string CreateFingerprint(NormalizedCancelFreeze cancellation)
    {
        var envelope = cancellation.Envelope;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = envelope.Reason,
            EnvelopeComment = envelope.Comment,
            cancellation.FreezeId,
            cancellation.EntryBatchId,
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

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        NormalizedAddFreeze freeze,
        string fingerprint,
        out Guid freezeId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == freeze.Envelope.Actor.AccountId.Value
            && string.Equals(
                record.ResultFingerprint,
                fingerprint,
                StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId == freeze.ClientId
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            freezeId = primaryEntityId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        freezeId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        NormalizedCancelFreeze cancellation,
        string fingerprint,
        out Guid cancellationId,
        out Guid clientId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == cancellation.Envelope.Actor.AccountId.Value
            && string.Equals(
                record.ResultFingerprint,
                fingerprint,
                StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId is { } rereadTargetId
            && rereadTargetId != Guid.Empty
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            cancellationId = primaryEntityId;
            clientId = rereadTargetId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        cancellationId = Guid.Empty;
        clientId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedAddFreeze freeze,
        DateTimeOffset recordedAt,
        Guid freezeId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = freeze.Envelope;
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = envelope.IdempotencyKey!,
            RequestCorrelationId = envelope.RequestCorrelationId.Value,
            AccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            AccountKind = MapAccountKind(envelope.Actor.AccountKind),
            SessionId = envelope.Actor.SessionId.Value,
            DeviceLabel = envelope.Actor.DeviceLabel,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = freezeId,
            RereadTargetId = freeze.ClientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedCancelFreeze cancellation,
        DateTimeOffset recordedAt,
        Guid cancellationId,
        Guid clientId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = cancellation.Envelope;
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = envelope.IdempotencyKey!,
            RequestCorrelationId = envelope.RequestCorrelationId.Value,
            AccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            AccountKind = MapAccountKind(envelope.Actor.AccountKind),
            SessionId = envelope.Actor.SessionId.Value,
            DeviceLabel = envelope.Actor.DeviceLabel,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = cancellationId,
            RereadTargetId = clientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.DuplicateSubmission,
                "AddFreeze with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Membership, Visit or Freeze state changed concurrently. Refresh canonical state and try again.");
            return true;
        }

        result = null!;
        return false;
    }

    internal static bool TryMapCancelFreezePostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == FreezeCancellationUniqueConstraint)
        {
            result = AlreadyCanceled();
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.DuplicateSubmission,
                "CancelFreeze with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Membership or Freeze state changed concurrently. Refresh canonical state and try again.");
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

    internal static async Task<CommandResult> RollBackAndReturnAsync(
        BodyLifeDbContext dbContext,
        IDbContextTransaction transaction,
        CommandResult result)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        return result;
    }

    internal static async Task RollBackAndClearAsync(
        BodyLifeDbContext dbContext,
        IDbContextTransaction transaction)
    {
        await transaction.RollbackAsync(CancellationToken.None);
        dbContext.ChangeTracker.Clear();
    }

    internal static CommandResult Success(
        Guid freezeId,
        NormalizedAddFreeze freeze,
        AuditEntryId auditEntryId)
    {
        return CommandResult.Success(
            new EntityId(AddFreezeCommand.PrimaryEntityType, freezeId),
            new EntityId(
                AddFreezeCommand.CanonicalRereadEntityType,
                freeze.ClientId),
            [new EntityId(AddFreezeCommand.MembershipEntityType, freeze.MembershipId)],
            auditEntryId: auditEntryId);
    }

    internal static CommandResult CancelFreezeSuccess(
        Guid cancellationId,
        Guid freezeId,
        Guid clientId,
        AuditEntryId auditEntryId,
        bool changedAfterClose)
    {
        return CommandResult.Success(
            new EntityId(CancelFreezeCommand.PrimaryEntityType, cancellationId),
            new EntityId(CancelFreezeCommand.CanonicalRereadEntityType, clientId),
            [new EntityId(CancelFreezeCommand.SourceFreezeEntityType, freezeId)],
            auditEntryId: auditEntryId,
            changedAfterClose: changedAfterClose);
    }

    internal static CommandResult DuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete AddFreeze request.",
            "idempotencyKey");
    }

    internal static CommandResult CancelFreezeDuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete CancelFreeze request.",
            "idempotencyKey");
    }

    internal static CommandResult AlreadyCanceled()
    {
        return Error(
            CommandErrorCode.AlreadyCanceled,
            "Freeze has already been canceled.",
            "freezeId");
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

    internal static string MapEntryOrigin(EntryOrigin entryOrigin)
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

    private static CommandResult? ValidateAndNormalizeEnvelope(
        CommandEnvelope envelope,
        out CommandEnvelope? canonicalEnvelope)
    {
        canonicalEnvelope = null;
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
            return ValidationError("Entry origin is not supported.", "entryOrigin");
        }

        if (envelope.OccurredAt is null)
        {
            return ValidationError(
                "Occurred_at is required for a Freeze.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Freeze reason is required.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Freeze reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment?.Length > CommentMaxLength)
        {
            return ValidationError(
                $"Freeze comment must be {CommentMaxLength} characters or fewer.",
                "comment");
        }

        canonicalEnvelope = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(requestCorrelationId),
            envelope.EntryOrigin,
            envelope.OccurredAt.Value.ToUniversalTime(),
            idempotencyKey,
            reason,
            comment);
        return null;
    }

    private static CommandResult? ValidateAndNormalizeCancellationEnvelope(
        CommandEnvelope envelope,
        out CommandEnvelope? canonicalEnvelope)
    {
        canonicalEnvelope = null;
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
            return ValidationError("Entry origin is not supported.", "entryOrigin");
        }

        if (envelope.OccurredAt is null)
        {
            return ValidationError(
                "Occurred_at is required for Freeze cancellation.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Reason is required for Freeze cancellation.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Freeze cancellation reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment?.Length > CommentMaxLength)
        {
            return ValidationError(
                $"Freeze cancellation comment must be {CommentMaxLength} characters or fewer.",
                "comment");
        }

        canonicalEnvelope = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(requestCorrelationId),
            envelope.EntryOrigin,
            envelope.OccurredAt.Value.ToUniversalTime(),
            idempotencyKey,
            reason,
            comment);
        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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
}

internal sealed record NormalizedAddFreeze(
    Guid ClientId,
    Guid MembershipId,
    DateRange Range,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);

internal sealed record NormalizedCancelFreeze(
    Guid FreezeId,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);
