using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal static class PaperFallbackCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const string SheetUniqueConstraint =
        "ux_entry_batches_paper_sheet_number";
    private const string LineUniqueConstraint =
        "ux_entry_batch_rows_batch_line_number";
    private const string RowParentConstraint =
        "ck_entry_batch_rows_parent";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 2000;
    private const int PaperSheetNumberMaxLength = 128;
    private const int ExplanationMaxLength = 2000;
    private const int NoteMaxLength = 2000;
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
                    AccountKind: AccountKind.NamedAdmin
                        or AccountKind.SharedReceptionAdmin,
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
        CreatePaperFallbackBatchCommand command,
        out NormalizedPaperFallbackBatch? normalized)
    {
        normalized = null;
        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (!IsSupportedDate(command.BusinessDateStart))
        {
            return ValidationError(
                "Business date start is required.",
                "businessDateStart");
        }

        if (!IsSupportedDate(command.BusinessDateEnd))
        {
            return ValidationError(
                "Business date end is required.",
                "businessDateEnd");
        }

        if (command.BusinessDateEnd < command.BusinessDateStart)
        {
            return ValidationError(
                "Business date end must be on or after the start.",
                "businessDateEnd");
        }

        var paperSheetNumber = NormalizeOptional(command.PaperSheetNumber)
            ?.ToUpperInvariant();
        if (paperSheetNumber is null
            || paperSheetNumber.Length > PaperSheetNumberMaxLength)
        {
            return ValidationError(
                $"Paper sheet number is required and must be {PaperSheetNumberMaxLength} characters or fewer.",
                "paperSheetNumber");
        }

        var note = NormalizeOptional(command.Note);
        if (note?.Length > NoteMaxLength)
        {
            return ValidationError(
                $"Batch note must be {NoteMaxLength} characters or fewer.",
                "note");
        }

        var occurredBusinessDate = BusinessTimeZone.GetBusinessDate(
            envelope!.OccurredAt!.Value);
        if (occurredBusinessDate < command.BusinessDateStart
            || occurredBusinessDate > command.BusinessDateEnd)
        {
            return ValidationError(
                "Batch occurred_at must fall inside its business date range.",
                "occurredAt");
        }

        normalized = new NormalizedPaperFallbackBatch(
            paperSheetNumber,
            command.BusinessDateStart,
            command.BusinessDateEnd,
            note,
            envelope);
        return null;
    }

    internal static CommandResult? ValidateAndNormalize(
        CreatePaperFallbackBatchRowCommand command,
        out NormalizedPaperFallbackBatchRow? normalized)
    {
        normalized = null;
        if (command.EntryBatchId == Guid.Empty)
        {
            return ValidationError("Entry batch id is required.", "entryBatchId");
        }

        if (command.LineNumber is <= 0)
        {
            return ValidationError(
                "Line number must be greater than zero when supplied.",
                "lineNumber");
        }

        if (!Enum.IsDefined(command.EventType))
        {
            return ValidationError(
                "Paper fallback event type is not supported.",
                "eventType");
        }

        var explanation = NormalizeOptional(command.Explanation);
        if (explanation is null || explanation.Length > ExplanationMaxLength)
        {
            return ValidationError(
                $"Row explanation is required and must be {ExplanationMaxLength} characters or fewer.",
                "explanation");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        normalized = new NormalizedPaperFallbackBatchRow(
            command.EntryBatchId,
            command.LineNumber,
            command.EventType,
            explanation,
            envelope!);
        return null;
    }

    internal static string CreateFingerprint(NormalizedPaperFallbackBatch batch)
    {
        return Hash(new
        {
            Envelope = FingerprintEnvelope(batch.Envelope),
            batch.PaperSheetNumber,
            batch.BusinessDateStart,
            batch.BusinessDateEnd,
            batch.Note,
        });
    }

    internal static string CreateFingerprint(NormalizedPaperFallbackBatchRow row)
    {
        return Hash(new
        {
            Envelope = FingerprintEnvelope(row.Envelope),
            row.EntryBatchId,
            row.RequestedLineNumber,
            EventType = MapEventType(row.EventType),
            row.Explanation,
        });
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

    internal static CommandResult ReplayBatchOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedPaperFallbackBatch batch,
        string fingerprint)
    {
        if (IsSuccessfulReplay(record, batch.Envelope, fingerprint)
            && record.PrimaryEntityId.HasValue
            && record.RereadTargetId == record.PrimaryEntityId
            && record.AuditEntryId.HasValue)
        {
            return BatchSuccess(
                record.PrimaryEntityId.Value,
                new AuditEntryId(record.AuditEntryId.Value));
        }

        return DuplicateSubmission();
    }

    internal static CommandResult ReplayRowOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedPaperFallbackBatchRow row,
        string fingerprint)
    {
        if (IsSuccessfulReplay(record, row.Envelope, fingerprint)
            && record.PrimaryEntityId.HasValue
            && record.RereadTargetId == row.EntryBatchId
            && record.AuditEntryId.HasValue)
        {
            return RowSuccess(
                record.PrimaryEntityId.Value,
                row.EntryBatchId,
                new AuditEntryId(record.AuditEntryId.Value));
        }

        return DuplicateSubmission();
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        CommandEnvelope envelope,
        DateTimeOffset recordedAt,
        Guid primaryEntityId,
        Guid rereadTargetId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
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
            EntryOrigin = "paper_fallback",
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = primaryEntityId,
            RereadTargetId = rereadTargetId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            result = exception.ConstraintName switch
            {
                IdempotencyUniqueConstraint => Error(
                    CommandErrorCode.DuplicateSubmission,
                    "Paper fallback command with this idempotency key is already in progress or completed.",
                    "idempotencyKey"),
                SheetUniqueConstraint => Error(
                    CommandErrorCode.DuplicateSubmission,
                    "Paper sheet number has already been registered.",
                    "paperSheetNumber"),
                LineUniqueConstraint => Error(
                    CommandErrorCode.DuplicateSubmission,
                    "Paper sheet line number has already been registered.",
                    "lineNumber"),
                _ => null!,
            };
            return result is not null;
        }

        if (exception.SqlState == PostgresErrorCodes.CheckViolation
            && exception.ConstraintName == RowParentConstraint)
        {
            result = ValidationError(
                "Paper row occurred_at must fall inside its batch business date range.",
                "occurredAt");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Paper fallback metadata changed concurrently. Refresh and try again.");
            return true;
        }

        result = null!;
        return false;
    }

    internal static bool IsUniqueViolation(PostgresException exception) =>
        exception.SqlState == PostgresErrorCodes.UniqueViolation;

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

    internal static CommandResult BatchSuccess(
        Guid batchId,
        AuditEntryId auditEntryId)
    {
        var id = new EntityId(PaperFallbackAuditActions.BatchEntityType, batchId);
        return CommandResult.Success(id, id, auditEntryId: auditEntryId);
    }

    internal static CommandResult RowSuccess(
        Guid rowId,
        Guid batchId,
        AuditEntryId auditEntryId)
    {
        return CommandResult.Success(
            new EntityId(PaperFallbackAuditActions.RowEntityType, rowId),
            new EntityId(PaperFallbackAuditActions.BatchEntityType, batchId),
            relatedEntityIds:
            [
                new EntityId(PaperFallbackAuditActions.BatchEntityType, batchId),
            ],
            auditEntryId: auditEntryId);
    }

    internal static CommandResult DuplicateSubmission() => Error(
        CommandErrorCode.DuplicateSubmission,
        "Idempotency key has already been used by a different or incomplete paper fallback request.",
        "idempotencyKey");

    internal static CommandResult ValidationError(string message, string? field) =>
        Error(CommandErrorCode.ValidationFailed, message, field);

    internal static CommandResult Error(
        CommandErrorCode code,
        string message,
        string? field = null)
    {
        return CommandResult.Error([new CommandError(code, message, field)]);
    }

    internal static string MapEventType(PaperFallbackEventType eventType)
    {
        return eventType switch
        {
            PaperFallbackEventType.Visit => "visit",
            PaperFallbackEventType.Payment => "payment",
            PaperFallbackEventType.Freeze => "freeze",
            PaperFallbackEventType.MembershipSale => "membership_sale",
            PaperFallbackEventType.NegativeCoverage => "negative_coverage",
            PaperFallbackEventType.CorrectionOrCancellation
                => "correction_or_cancellation",
            _ => throw new ArgumentOutOfRangeException(
                nameof(eventType),
                eventType,
                null),
        };
    }

    private static CommandResult? ValidateAndNormalizeEnvelope(
        CommandEnvelope? envelope,
        out CommandEnvelope? normalized)
    {
        normalized = null;
        if (envelope?.Actor is null)
        {
            return ValidationError("Command envelope is required.", "envelope");
        }

        var idempotencyKey = NormalizeOptional(envelope.IdempotencyKey);
        if (idempotencyKey is null || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ValidationError(
                $"Idempotency key is required and must be {IdempotencyKeyMaxLength} characters or fewer.",
                "idempotencyKey");
        }

        var correlationId = NormalizeOptional(envelope.RequestCorrelationId.Value);
        if (correlationId is null || correlationId.Length > CorrelationIdMaxLength)
        {
            return ValidationError(
                $"Request correlation id is required and must be {CorrelationIdMaxLength} characters or fewer.",
                "requestCorrelationId");
        }

        if (envelope.EntryOrigin != EntryOrigin.PaperFallback)
        {
            return ValidationError(
                "Paper fallback metadata requires paper_fallback entry origin.",
                "entryOrigin");
        }

        if (envelope.OccurredAt is null
            || !BusinessTimeZone.TryNormalizeUtcInstant(
                envelope.OccurredAt.Value,
                out var occurredAt))
        {
            return ValidationError(
                "A valid occurred_at is required for paper fallback metadata.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);
        if (reason is null && comment is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Paper fallback metadata requires a reason or comment.",
                "reason");
        }

        if (reason?.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (comment?.Length > CommentMaxLength)
        {
            return ValidationError(
                $"Comment must be {CommentMaxLength} characters or fewer.",
                "comment");
        }

        var deviceLabel = NormalizeOptional(envelope.Actor.DeviceLabel);
        if (deviceLabel?.Length > DeviceLabelMaxLength)
        {
            return ValidationError(
                $"Device label must be {DeviceLabelMaxLength} characters or fewer.",
                "deviceLabel");
        }

        normalized = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(correlationId),
            EntryOrigin.PaperFallback,
            occurredAt,
            idempotencyKey,
            reason,
            comment);
        return null;
    }

    private static bool IsSuccessfulReplay(
        CommandIdempotencyRecord record,
        CommandEnvelope envelope,
        string fingerprint)
    {
        return record.Status == SucceededIdempotencyStatus
            && record.AccountId == envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal);
    }

    private static object FingerprintEnvelope(CommandEnvelope envelope) => new
    {
        ActorAccountId = envelope.Actor.AccountId.Value,
        ActorRole = MapActorRole(envelope.Actor.Role),
        ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
        ActorSessionId = envelope.Actor.SessionId.Value,
        envelope.Actor.DeviceLabel,
        envelope.RequestCorrelationId.Value,
        envelope.OccurredAt,
        envelope.Reason,
        envelope.Comment,
    };

    private static string Hash(object payload)
    {
        return Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
    }

    private static bool IsSupportedDate(DateOnly value) =>
        value != default && value != DateOnly.MaxValue;

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
            _ => throw new ArgumentOutOfRangeException(
                nameof(accountKind),
                accountKind,
                null),
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

internal sealed record NormalizedPaperFallbackBatch(
    string PaperSheetNumber,
    DateOnly BusinessDateStart,
    DateOnly BusinessDateEnd,
    string? Note,
    CommandEnvelope Envelope);

internal sealed record NormalizedPaperFallbackBatchRow(
    Guid EntryBatchId,
    int? RequestedLineNumber,
    PaperFallbackEventType EventType,
    string Explanation,
    CommandEnvelope Envelope);
