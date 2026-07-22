using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class MembershipCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint = "ux_command_idempotency_keys_command_key";
    private const string ActiveOpeningStateUniqueConstraint =
        "ux_membership_opening_states_active_membership";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 2000;
    private const int SourceReferenceMaxLength = 500;
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

    internal static CommandResult? ValidateAndNormalizeCreateOpeningState(
        CreateMembershipOpeningStateCommand command,
        out NormalizedMembershipOpeningStateCreate? normalizedCreate)
    {
        normalizedCreate = null;

        if (command.MembershipId == Guid.Empty)
        {
            return ValidationError("Membership id is required.", "membershipId");
        }

        if (command.OpeningAsOfDate == default)
        {
            return ValidationError("Opening as-of date is required.", "openingAsOfDate");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var normalizedEnvelope);

        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        var sourceReference = command.SourceReference?.Trim();
        if (string.IsNullOrWhiteSpace(sourceReference)
            || sourceReference.Length > SourceReferenceMaxLength)
        {
            return ValidationError(
                $"Source reference is required and must be {SourceReferenceMaxLength} characters or fewer.",
                "sourceReference");
        }

        if (command.EntryBatchId == Guid.Empty)
        {
            return ValidationError(
                "Entry batch id must be a non-empty identifier when supplied.",
                "entryBatchId");
        }

        MembershipOpeningState declaration;

        try
        {
            declaration = MembershipOpeningState.FromDeclaration(
                command.OpeningAsOfDate,
                command.DeclaredRemainingVisits,
                command.KnownEffectiveEndDate,
                command.KnownExtensionDays);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, exception.ParamName);
        }

        normalizedCreate = new NormalizedMembershipOpeningStateCreate(
            command.MembershipId,
            declaration,
            sourceReference,
            command.EntryBatchId,
            normalizedEnvelope!);
        return null;
    }

    internal static string CreateOpeningStateFingerprint(
        NormalizedMembershipOpeningStateCreate create)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = create.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(create.Envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(create.Envelope.Actor.AccountKind),
            ActorSessionId = create.Envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(create.Envelope.EntryOrigin),
            create.Envelope.OccurredAt,
            EnvelopeReason = create.Envelope.Reason,
            EnvelopeComment = create.Envelope.Comment,
            create.MembershipId,
            create.Declaration.OpeningAsOfDate,
            create.Declaration.DeclaredRemainingVisits,
            create.Declaration.KnownEffectiveEndDate,
            create.Declaration.KnownExtensionDays,
            create.SourceReference,
            create.EntryBatchId,
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
            && record.PrimaryEntityId.Value != Guid.Empty
            && record.RereadTargetId.HasValue
            && record.RereadTargetId.Value != Guid.Empty
            && record.AuditEntryId.HasValue)
        {
            return Success(
                record.PrimaryEntityId.Value,
                record.RereadTargetId.Value,
                new AuditEntryId(record.AuditEntryId.Value));
        }

        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete membership request.",
            "idempotencyKey");
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedMembershipOpeningStateCreate create,
        DateTimeOffset recordedAt,
        Guid openingStateId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = create.Envelope.IdempotencyKey!,
            RequestCorrelationId = create.Envelope.RequestCorrelationId.Value!,
            AccountId = create.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(create.Envelope.Actor.Role),
            AccountKind = MapAccountKind(create.Envelope.Actor.AccountKind),
            SessionId = create.Envelope.Actor.SessionId.Value,
            DeviceLabel = create.Envelope.Actor.DeviceLabel,
            EntryOrigin = MapEntryOrigin(create.Envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = openingStateId,
            RereadTargetId = create.MembershipId,
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
                "Membership command with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == ActiveOpeningStateUniqueConstraint)
        {
            result = Error(
                CommandErrorCode.StaleState,
                "An active opening state already exists. Refresh canonical membership state.",
                "membershipId");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Membership state changed concurrently. Refresh canonical state and try again.");
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

    internal static CommandResult Success(
        Guid openingStateId,
        Guid membershipId,
        AuditEntryId auditEntryId)
    {
        var primaryEntityId = new EntityId(
            MembershipAuditActions.OpeningStateEntityType,
            openingStateId);
        var rereadTargetId = new EntityId(
            CreateMembershipOpeningStateCommand.CanonicalRereadEntityType,
            membershipId);
        return CommandResult.Success(
            primaryEntityId,
            rereadTargetId,
            auditEntryId: auditEntryId);
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
        out CommandEnvelope? normalizedEnvelope)
    {
        normalizedEnvelope = null;
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

        if (envelope.EntryOrigin != EntryOrigin.ManualBackfill)
        {
            return ValidationError(
                "Membership opening state requires manual_backfill entry origin.",
                "entryOrigin");
        }

        if (envelope.OccurredAt is null)
        {
            return ValidationError(
                "Occurred_at is required for membership opening-state backfill.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);

        if (reason is null || reason.Length > AuditReasonMaxLength)
        {
            return ValidationError(
                $"Reason is required and must be {AuditReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (comment?.Length > AuditCommentMaxLength)
        {
            return ValidationError(
                $"Envelope comment must be {AuditCommentMaxLength} characters or fewer.",
                "envelope.comment");
        }

        if (!BusinessTimeZone.TryNormalizeUtcInstant(envelope.OccurredAt.Value, out var occurredAt))
        {
            return ValidationError(
                "Occurred_at is outside the supported business-calendar range.",
                "occurredAt");
        }

        normalizedEnvelope = new CommandEnvelope(
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

internal sealed record NormalizedMembershipOpeningStateCreate(
    Guid MembershipId,
    MembershipOpeningState Declaration,
    string SourceReference,
    Guid? EntryBatchId,
    CommandEnvelope Envelope)
{
    public string IdempotencyKey => Envelope.IdempotencyKey!;

    public string Reason => Envelope.Reason!;
}
