using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class NonWorkingDayCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int EnvelopeTextMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static bool IsOwnerActorShape(ActorContext? actor)
    {
        return actor is
        {
            Role: ActorRole.Owner,
            AccountKind: AccountKind.Owner,
        }
            && actor.AccountId.Value != Guid.Empty
            && actor.SessionId.Value != Guid.Empty;
    }

    internal static CommandResult? ValidateAndNormalize(
        AddNonWorkingDayCommand command,
        out NormalizedAddNonWorkingDay? normalizedCommand)
    {
        normalizedCommand = null;

        if (command.Period.StartDate == default || command.Period.EndDate == default)
        {
            return ValidationError("Non-working period dates are required.", "period");
        }

        NonWorkingDayPreviewInput input;
        try
        {
            input = new NonWorkingDayPreviewInput(
                command.Period,
                command.ReasonCode!,
                command.ReasonComment);
        }
        catch (ArgumentNullException exception)
            when (exception.ParamName == "reasonCode")
        {
            return ValidationError("Reason code is required.", "reasonCode");
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonCode")
        {
            return ValidationError(exception.Message, "reasonCode");
        }
        catch (ArgumentException exception)
            when (exception.ParamName == "reasonComment")
        {
            return ValidationError(exception.Message, "reasonComment");
        }

        if (string.IsNullOrWhiteSpace(command.ConfirmationToken)
            || command.ConfirmationToken != command.ConfirmationToken.Trim()
            || command.ConfirmationToken.Length
                > NonWorkingDayPreviewConfirmation.MaxTokenLength)
        {
            return ValidationError(
                "A canonical preview confirmation token is required.",
                "confirmationToken");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var canonicalEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        normalizedCommand = new NormalizedAddNonWorkingDay(
            input,
            command.ConfirmationToken,
            canonicalEnvelope!);
        return null;
    }

    internal static string CreateFingerprint(NormalizedAddNonWorkingDay command)
    {
        var envelope = command.Envelope;
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
            command.Input.Period.StartDate,
            command.Input.Period.EndDate,
            command.Input.ReasonCode,
            command.Input.ReasonComment,
            command.ConfirmationToken,
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
        NormalizedAddNonWorkingDay command,
        string fingerprint,
        out Guid periodId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == command.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId == primaryEntityId
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            periodId = primaryEntityId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        periodId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static async Task<IReadOnlyList<Guid>> ReadAppliedMembershipIdsAsync(
        BodyLifeDbContext dbContext,
        Guid periodId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<NonWorkingPeriodApplicationRecord>()
            .AsNoTracking()
            .Where(application => application.NonWorkingPeriodId == periodId)
            .OrderBy(application => application.MembershipId)
            .Select(application => application.MembershipId)
            .ToArrayAsync(cancellationToken);
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedAddNonWorkingDay command,
        DateTimeOffset recordedAt,
        Guid periodId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = command.Envelope;
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
            PrimaryEntityId = periodId,
            RereadTargetId = periodId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static bool IsIdempotencyUniqueViolation(PostgresException exception)
    {
        return exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint;
    }

    internal static bool IsUniqueViolation(PostgresException exception)
    {
        return exception.SqlState == PostgresErrorCodes.UniqueViolation;
    }

    internal static bool IsRetryableConcurrencyFailure(PostgresException exception)
    {
        return IsUniqueViolation(exception)
            || exception.SqlState is PostgresErrorCodes.SerializationFailure
                or PostgresErrorCodes.DeadlockDetected
                or PostgresErrorCodes.LockNotAvailable;
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (IsIdempotencyUniqueViolation(exception))
        {
            result = DuplicateSubmission();
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Membership or non-working period state changed concurrently. Refresh the preview and try again.");
            return true;
        }

        result = null!;
        return false;
    }

    internal static CommandResult ConcurrencyConflict()
    {
        return Error(
            CommandErrorCode.ConcurrencyConflict,
            "Membership or non-working period state changed concurrently. Refresh the preview and try again.");
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
        Guid periodId,
        IReadOnlyList<Guid> membershipIds,
        AuditEntryId auditEntryId)
    {
        var periodEntityId = new EntityId(
            AddNonWorkingDayCommand.PrimaryEntityType,
            periodId);
        return CommandResult.Success(
            periodEntityId,
            periodEntityId,
            membershipIds
                .Select(id => new EntityId(AddNonWorkingDayCommand.MembershipEntityType, id))
                .ToArray(),
            auditEntryId: auditEntryId);
    }

    internal static CommandResult DuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete AddNonWorkingDay request.",
            "idempotencyKey");
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
        if (envelope is null)
        {
            return ValidationError("Command envelope is required.", "envelope");
        }

        var idempotencyKey = envelope.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ValidationError(
                $"Idempotency key is required and must be {IdempotencyKeyMaxLength} characters or fewer.",
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

        var reason = NormalizeOptional(envelope.Reason);
        if (reason?.Length > EnvelopeTextMaxLength)
        {
            return ValidationError(
                $"Envelope reason must be {EnvelopeTextMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment?.Length > EnvelopeTextMaxLength)
        {
            return ValidationError(
                $"Envelope comment must be {EnvelopeTextMaxLength} characters or fewer.",
                "comment");
        }

        DateTimeOffset? occurredAt = null;
        if (envelope.OccurredAt is { } submittedOccurredAt)
        {
            if (!BusinessTimeZone.TryNormalizeUtcInstant(submittedOccurredAt, out var normalizedOccurredAt))
            {
                return ValidationError(
                    "Occurred_at is outside the supported business-calendar range.",
                    "occurredAt");
            }

            occurredAt = normalizedOccurredAt;
        }

        canonicalEnvelope = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(requestCorrelationId),
            envelope.EntryOrigin,
            occurredAt,
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

internal sealed record NormalizedAddNonWorkingDay(
    NonWorkingDayPreviewInput Input,
    string ConfirmationToken,
    CommandEnvelope Envelope);
