using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

internal static class VisitCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const string VisitCancellationUniqueConstraint =
        "ux_visit_cancellations_visit_id";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int VisitCommentMaxLength = 1000;
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
        MarkVisitCommand command,
        out NormalizedMarkVisit? normalizedVisit)
    {
        normalizedVisit = null;

        if (command.ClientId == Guid.Empty)
        {
            return ValidationError("Client id is required.", "clientId");
        }

        if (!Enum.IsDefined(command.VisitKind))
        {
            return ValidationError("Visit kind is not supported.", "visitKind");
        }

        if (command.Acknowledgements is null)
        {
            return ValidationError(
                "Membership acknowledgement collection is required.",
                "acknowledgements");
        }

        var acknowledgements = command.Acknowledgements.ToArray();
        if (acknowledgements.Any(acknowledgement => !Enum.IsDefined(acknowledgement)))
        {
            return ValidationError(
                "Membership acknowledgement is not supported.",
                "acknowledgements");
        }

        if (acknowledgements.Distinct().Count() != acknowledgements.Length)
        {
            return ValidationError(
                "Each membership acknowledgement can be supplied only once.",
                "acknowledgements");
        }

        if (command.VisitKind == VisitKind.Membership)
        {
            if (command.MembershipId is null || command.MembershipId == Guid.Empty)
            {
                return ValidationError(
                    "Membership id is required for a membership Visit.",
                    "membershipId");
            }
        }
        else
        {
            if (command.MembershipId is not null)
            {
                return ValidationError(
                    "One-off and trial Visits cannot select a membership.",
                    "membershipId");
            }

            if (acknowledgements.Length > 0)
            {
                return ValidationError(
                    "One-off and trial Visits cannot carry membership acknowledgements.",
                    "acknowledgements");
            }
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
                "Normal Visit entry cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        normalizedVisit = new NormalizedMarkVisit(
            command.ClientId,
            command.VisitKind,
            command.MembershipId,
            acknowledgements.OrderBy(acknowledgement => acknowledgement).ToArray(),
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static CommandResult? ValidateAndNormalize(
        CancelVisitCommand command,
        out NormalizedCancelVisit? normalizedCancellation)
    {
        normalizedCancellation = null;

        if (command.VisitId == Guid.Empty)
        {
            return ValidationError("Visit id is required.", "visitId");
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
                "Normal Visit cancellation cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        normalizedCancellation = new NormalizedCancelVisit(
            command.VisitId,
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static string CreateFingerprint(NormalizedMarkVisit visit)
    {
        var envelope = visit.Envelope;
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
            visit.ClientId,
            VisitKind = MapVisitKind(visit.VisitKind),
            visit.MembershipId,
            Acknowledgements = visit.Acknowledgements.Select(MapAcknowledgement),
            visit.EntryBatchId,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static string CreateFingerprint(NormalizedCancelVisit cancellation)
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
            cancellation.VisitId,
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
        NormalizedMarkVisit visit,
        string fingerprint,
        out Guid visitId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == visit.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId == visit.ClientId
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            visitId = primaryEntityId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        visitId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        NormalizedCancelVisit cancellation,
        string fingerprint,
        out Guid cancellationId,
        out Guid clientId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == cancellation.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
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
        NormalizedMarkVisit visit,
        DateTimeOffset recordedAt,
        Guid visitId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = visit.Envelope;
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
            PrimaryEntityId = visitId,
            RereadTargetId = visit.ClientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedCancelVisit cancellation,
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
                "MarkVisit with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Visit or Membership state changed concurrently. Refresh canonical state and try again.");
            return true;
        }

        result = null!;
        return false;
    }

    internal static bool TryMapCancelVisitPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == VisitCancellationUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.AlreadyCanceled,
                "Visit has already been canceled.",
                "visitId");
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.DuplicateSubmission,
                "CancelVisit with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Visit or Membership state changed concurrently. Refresh canonical state and try again.");
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
        Guid visitId,
        NormalizedMarkVisit visit,
        AuditEntryId auditEntryId,
        IReadOnlyList<string>? warnings = null)
    {
        EntityId[] relatedEntityIds = visit.MembershipId is { } membershipId
            ? [new EntityId(VisitAuditActions.MembershipEntityType, membershipId)]
            : [];

        return CommandResult.Success(
            new EntityId(MarkVisitCommand.PrimaryEntityType, visitId),
            new EntityId(MarkVisitCommand.CanonicalRereadEntityType, visit.ClientId),
            relatedEntityIds,
            warnings,
            auditEntryId);
    }

    internal static CommandResult CancelVisitSuccess(
        Guid cancellationId,
        Guid visitId,
        Guid clientId,
        AuditEntryId auditEntryId,
        bool changedAfterClose)
    {
        return CommandResult.Success(
            new EntityId(CancelVisitCommand.PrimaryEntityType, cancellationId),
            new EntityId(CancelVisitCommand.CanonicalRereadEntityType, clientId),
            [new EntityId(CancelVisitCommand.SourceVisitEntityType, visitId)],
            auditEntryId: auditEntryId,
            changedAfterClose: changedAfterClose);
    }

    internal static CommandResult DuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete MarkVisit request.",
            "idempotencyKey");
    }

    internal static CommandResult CancelVisitDuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete CancelVisit request.",
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

    internal static string MapVisitKind(VisitKind visitKind)
    {
        return visitKind switch
        {
            VisitKind.Membership => "membership",
            VisitKind.OneOff => "one_off",
            VisitKind.Trial => "trial",
            _ => throw new ArgumentOutOfRangeException(nameof(visitKind), visitKind, null),
        };
    }

    internal static string MapAcknowledgement(
        MembershipVisitAcknowledgement acknowledgement)
    {
        return acknowledgement switch
        {
            MembershipVisitAcknowledgement.Expired => "expired",
            MembershipVisitAcknowledgement.ZeroRemaining => "zero_remaining",
            MembershipVisitAcknowledgement.NegativeRemaining => "negative_remaining",
            _ => throw new ArgumentOutOfRangeException(
                nameof(acknowledgement),
                acknowledgement,
                null),
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
            return ValidationError("Occurred_at is required for a Visit.", "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);
        if (reason?.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (comment?.Length > VisitCommentMaxLength)
        {
            return ValidationError(
                $"Visit comment must be {VisitCommentMaxLength} characters or fewer.",
                "comment");
        }

        if (envelope.EntryOrigin != EntryOrigin.Normal
            && reason is null
            && comment is null)
        {
            return ValidationError(
                "Backdated, fallback or import Visit requires a reason or comment.",
                "entryOrigin");
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
                "Occurred_at is required for Visit cancellation.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Reason is required for Visit cancellation.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment?.Length > VisitCommentMaxLength)
        {
            return ValidationError(
                $"Cancellation comment must be {VisitCommentMaxLength} characters or fewer.",
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

internal sealed record NormalizedMarkVisit(
    Guid ClientId,
    VisitKind VisitKind,
    Guid? MembershipId,
    IReadOnlyList<MembershipVisitAcknowledgement> Acknowledgements,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);

internal sealed record NormalizedCancelVisit(
    Guid VisitId,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);
