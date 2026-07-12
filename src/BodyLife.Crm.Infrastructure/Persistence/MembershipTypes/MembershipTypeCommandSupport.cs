using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

internal static class MembershipTypeCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint = "ux_command_idempotency_keys_command_key";
    private const string OwnerAccountType = "owner";
    private const string OwnerRole = "owner";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 2000;
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

    internal static async Task<bool> IsCanonicalOwnerAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accountIsActiveOwner = await dbContext.Set<AccountRecord>()
            .AsNoTracking()
            .AnyAsync(
                account => account.Id == actor.AccountId.Value
                    && account.IsActive
                    && account.AccountType == OwnerAccountType
                    && account.Role == OwnerRole,
                cancellationToken);

        if (!accountIsActiveOwner)
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

    internal static CommandResult? ValidateAndNormalizeCreate(
        CreateMembershipTypeCommand command,
        out NormalizedMembershipTypeCreate? normalizedCreate)
    {
        normalizedCreate = null;
        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var normalizedEnvelope);

        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        MembershipTypeCatalogValues catalogValues;

        try
        {
            catalogValues = MembershipTypeCatalogRules.NormalizeAndValidate(
                command.Name,
                command.DurationDays,
                command.VisitsLimit,
                command.Price,
                command.Comment);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, exception.ParamName);
        }

        normalizedCreate = new NormalizedMembershipTypeCreate(
            catalogValues,
            command.IsActive,
            normalizedEnvelope!);
        return null;
    }

    internal static CommandResult? ValidateAndNormalizeEdit(
        EditMembershipTypeCommand command,
        out NormalizedMembershipTypeEdit? normalizedEdit)
    {
        normalizedEdit = null;

        if (command.MembershipTypeId == Guid.Empty)
        {
            return ValidationError("Membership type id is required.", "membershipTypeId");
        }

        if (command.ExpectedUpdatedAt == default)
        {
            return ValidationError(
                "Expected updated_at is required.",
                "expectedUpdatedAt");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var normalizedEnvelope);

        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (normalizedEnvelope!.Reason is null && normalizedEnvelope.Comment is null)
        {
            return ValidationError(
                "Reason or command comment is required to edit a membership type.",
                "reason");
        }

        MembershipTypeCatalogValues catalogValues;

        try
        {
            catalogValues = MembershipTypeCatalogRules.NormalizeAndValidate(
                command.Name,
                command.DurationDays,
                command.VisitsLimit,
                command.Price,
                command.Comment);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, exception.ParamName);
        }

        normalizedEdit = new NormalizedMembershipTypeEdit(
            command.MembershipTypeId,
            command.ExpectedUpdatedAt.ToUniversalTime(),
            catalogValues,
            normalizedEnvelope);
        return null;
    }

    internal static string CreateFingerprint(
        CommandEnvelope envelope,
        NormalizedMembershipTypeCreate normalizedCreate)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = normalizedCreate.Envelope.Reason,
            EnvelopeComment = normalizedCreate.Envelope.Comment,
            normalizedCreate.CatalogValues.Name,
            normalizedCreate.CatalogValues.DurationDays,
            normalizedCreate.CatalogValues.VisitsLimit,
            PriceAmount = normalizedCreate.CatalogValues.Price.Amount,
            PriceCurrency = normalizedCreate.CatalogValues.Price.Currency,
            normalizedCreate.CatalogValues.Comment,
            normalizedCreate.IsActive,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static string CreateEditFingerprint(
        CommandEnvelope envelope,
        NormalizedMembershipTypeEdit normalizedEdit)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = normalizedEdit.Envelope.Reason,
            EnvelopeComment = normalizedEdit.Envelope.Comment,
            normalizedEdit.MembershipTypeId,
            normalizedEdit.ExpectedUpdatedAt,
            normalizedEdit.CatalogValues.Name,
            normalizedEdit.CatalogValues.DurationDays,
            normalizedEdit.CatalogValues.VisitsLimit,
            PriceAmount = normalizedEdit.CatalogValues.Price.Amount,
            PriceCurrency = normalizedEdit.CatalogValues.Price.Currency,
            normalizedEdit.CatalogValues.Comment,
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
            "Idempotency key has already been used by a different or incomplete membership type request.",
            "idempotencyKey");
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        CommandEnvelope envelope,
        NormalizedMembershipTypeCreate normalizedCreate,
        DateTimeOffset recordedAt,
        Guid membershipTypeId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return CreateSucceededIdempotencyRecord(
            commandName,
            envelope,
            normalizedCreate.Envelope,
            recordedAt,
            membershipTypeId,
            auditEntryId,
            fingerprint);
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        CommandEnvelope envelope,
        NormalizedMembershipTypeCommandEnvelope normalizedEnvelope,
        DateTimeOffset recordedAt,
        Guid membershipTypeId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = normalizedEnvelope.IdempotencyKey,
            RequestCorrelationId = normalizedEnvelope.RequestCorrelationId,
            AccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            AccountKind = MapAccountKind(envelope.Actor.AccountKind),
            SessionId = envelope.Actor.SessionId.Value,
            DeviceLabel = normalizedEnvelope.DeviceLabel,
            EntryOrigin = MapEntryOrigin(envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = membershipTypeId,
            RereadTargetId = membershipTypeId,
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
                "Membership type command with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Membership type catalog changed concurrently. Refresh canonical state and try again.");
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

    internal static CommandResult Success(Guid membershipTypeId, AuditEntryId auditEntryId)
    {
        var entityId = new EntityId(MembershipTypeAuditActions.EntityType, membershipTypeId);
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

    private static CommandResult? ValidateAndNormalizeEnvelope(
        CommandEnvelope envelope,
        out NormalizedMembershipTypeCommandEnvelope? normalizedEnvelope)
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

        if (!Enum.IsDefined(envelope.EntryOrigin))
        {
            return ValidationError("Entry origin is invalid.", "entryOrigin");
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);

        if (reason?.Length > AuditReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {AuditReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (comment?.Length > AuditCommentMaxLength)
        {
            return ValidationError(
                $"Envelope comment must be {AuditCommentMaxLength} characters or fewer.",
                "envelope.comment");
        }

        if (envelope.EntryOrigin != EntryOrigin.Normal
            && (envelope.OccurredAt is null || (reason is null && comment is null)))
        {
            return ValidationError(
                "Non-normal membership type creation requires occurred_at and a reason or command comment.",
                "entryOrigin");
        }

        normalizedEnvelope = new NormalizedMembershipTypeCommandEnvelope(
            idempotencyKey,
            requestCorrelationId,
            deviceLabel,
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
            AccountKind.Owner => OwnerAccountType,
            AccountKind.NamedAdmin => "named_admin",
            AccountKind.SharedReceptionAdmin => "shared_reception_admin",
            _ => throw new ArgumentOutOfRangeException(nameof(accountKind), accountKind, null),
        };
    }

    private static string MapActorRole(ActorRole role)
    {
        return role switch
        {
            ActorRole.Owner => OwnerRole,
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

internal sealed record NormalizedMembershipTypeCreate(
    MembershipTypeCatalogValues CatalogValues,
    bool IsActive,
    NormalizedMembershipTypeCommandEnvelope Envelope);

internal sealed record NormalizedMembershipTypeEdit(
    Guid MembershipTypeId,
    DateTimeOffset ExpectedUpdatedAt,
    MembershipTypeCatalogValues CatalogValues,
    NormalizedMembershipTypeCommandEnvelope Envelope);

internal sealed record NormalizedMembershipTypeCommandEnvelope(
    string IdempotencyKey,
    string RequestCorrelationId,
    string? DeviceLabel,
    string? Reason,
    string? Comment);
