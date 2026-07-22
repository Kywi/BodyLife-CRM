using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal static class PaymentCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int PaymentCommentMaxLength = 1000;
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
        CreatePaymentCommand command,
        out NormalizedCreatePayment? normalizedPayment)
    {
        normalizedPayment = null;

        if (command.ClientId == Guid.Empty)
        {
            return ValidationError("Client id is required.", "clientId");
        }

        if (command.MembershipId == Guid.Empty)
        {
            return ValidationError(
                "Membership id must be a non-empty identifier when supplied.",
                "membershipId");
        }

        if (!Enum.IsDefined(command.PaymentContext))
        {
            return ValidationError(
                "Payment context is not supported.",
                "paymentContext");
        }

        if (command.PaymentContext == PaymentContext.NegativeClosure)
        {
            return ValidationError(
                "Negative closure requires its explicit workflow and cannot be recorded as a standalone Payment.",
                "paymentContext");
        }

        if (command.Amount.Amount <= 0)
        {
            return ValidationError("Payment amount must be greater than zero.", "amount");
        }

        Money amount;
        try
        {
            amount = new Money(command.Amount.Amount, command.Amount.Currency);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, "amount");
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
                "Normal Payment entry cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        normalizedPayment = new NormalizedCreatePayment(
            command.ClientId,
            command.MembershipId,
            amount,
            command.PaymentContext,
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static string CreateFingerprint(NormalizedCreatePayment payment)
    {
        var envelope = payment.Envelope;
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
            payment.ClientId,
            payment.MembershipId,
            Amount = payment.Amount.Amount,
            Currency = payment.Amount.Currency,
            PaymentContext = MapPaymentContext(payment.PaymentContext),
            payment.EntryBatchId,
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
        NormalizedCreatePayment payment,
        string fingerprint,
        out Guid paymentId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == payment.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId == payment.ClientId
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            paymentId = primaryEntityId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        paymentId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedCreatePayment payment,
        DateTimeOffset recordedAt,
        Guid paymentId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = payment.Envelope;
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
            PrimaryEntityId = paymentId,
            RereadTargetId = payment.ClientId,
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
                "CreatePayment with this idempotency key is already in progress or completed.",
                "idempotencyKey");
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = Error(
                CommandErrorCode.ConcurrencyConflict,
                "Client or Membership payment state changed concurrently. Refresh canonical state and try again.");
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
        Guid paymentId,
        NormalizedCreatePayment payment,
        AuditEntryId auditEntryId)
    {
        EntityId[] relatedEntityIds = payment.MembershipId is { } membershipId
            ? [new EntityId(PaymentAuditActions.MembershipEntityType, membershipId)]
            : [];

        return CommandResult.Success(
            new EntityId(CreatePaymentCommand.PrimaryEntityType, paymentId),
            new EntityId(
                CreatePaymentCommand.CanonicalRereadEntityType,
                payment.ClientId),
            relatedEntityIds,
            auditEntryId: auditEntryId);
    }

    internal static CommandResult DuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete CreatePayment request.",
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

    internal static string MapPaymentContext(PaymentContext paymentContext)
    {
        return paymentContext switch
        {
            PaymentContext.MembershipSale => "membership_sale",
            PaymentContext.OneOff => "one_off",
            PaymentContext.Trial => "trial",
            PaymentContext.NegativeClosure => "negative_closure",
            PaymentContext.Other => "other",
            _ => throw new ArgumentOutOfRangeException(
                nameof(paymentContext),
                paymentContext,
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
            return ValidationError(
                "Occurred_at is required for a Payment.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);
        if (reason?.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        if (comment?.Length > PaymentCommentMaxLength)
        {
            return ValidationError(
                $"Payment comment must be {PaymentCommentMaxLength} characters or fewer.",
                "comment");
        }

        if (envelope.EntryOrigin != EntryOrigin.Normal
            && reason is null
            && comment is null)
        {
            return ValidationError(
                "Backdated, fallback or import Payment requires a reason or comment.",
                "entryOrigin");
        }

        if (!BusinessTimeZone.TryNormalizeUtcInstant(envelope.OccurredAt.Value, out var occurredAt))
        {
            return ValidationError(
                "Occurred_at is outside the supported business-calendar range.",
                "occurredAt");
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

internal sealed record NormalizedCreatePayment(
    Guid ClientId,
    Guid? MembershipId,
    Money Amount,
    PaymentContext PaymentContext,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);
