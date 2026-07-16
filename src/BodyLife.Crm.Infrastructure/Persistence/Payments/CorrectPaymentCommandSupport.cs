using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

internal static class CorrectPaymentCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string IdempotencyUniqueConstraint =
        "ux_command_idempotency_keys_command_key";
    private const string CancellationUniqueConstraint =
        "ux_payment_cancellations_payment_id";
    private const string CorrectionOriginalUniqueConstraint =
        "ux_payment_corrections_original_payment_id";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static CommandResult? ValidateAndNormalize(
        CorrectPaymentCommand command,
        out NormalizedCorrectPayment? normalizedCorrection)
    {
        normalizedCorrection = null;

        if (command.OriginalPaymentId == Guid.Empty)
        {
            return ValidationError("Original Payment id is required.", "originalPaymentId");
        }

        if (!Enum.IsDefined(command.Mode))
        {
            return ValidationError("Payment correction mode is not supported.", "mode");
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
                "Normal Payment correction cannot reference a backfill or fallback batch.",
                "entryBatchId");
        }

        NormalizedPaymentReplacement? replacement = null;
        if (command.Mode == PaymentCorrectionMode.Cancel)
        {
            if (command.Replacement is not null)
            {
                return ValidationError(
                    "Cancel mode cannot include a replacement Payment.",
                    "replacement");
            }
        }
        else
        {
            if (command.Replacement is not { } submittedReplacement)
            {
                return ValidationError(
                    "Replace mode requires a replacement Payment.",
                    "replacement");
            }

            var replacementValidation = ValidateAndNormalizeReplacement(
                submittedReplacement,
                out replacement);
            if (replacementValidation is not null)
            {
                return replacementValidation;
            }
        }

        normalizedCorrection = new NormalizedCorrectPayment(
            command.OriginalPaymentId,
            command.Mode,
            replacement,
            command.EntryBatchId,
            canonicalEnvelope);
        return null;
    }

    internal static string CreateFingerprint(NormalizedCorrectPayment correction)
    {
        var envelope = correction.Envelope;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = PaymentCommandSupport.MapEntryOrigin(envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = envelope.Reason,
            EnvelopeComment = envelope.Comment,
            correction.OriginalPaymentId,
            Mode = correction.Mode.ToString(),
            Replacement = correction.Replacement is null
                ? null
                : new
                {
                    correction.Replacement.MembershipId,
                    Amount = correction.Replacement.Amount.Amount,
                    Currency = correction.Replacement.Amount.Currency,
                    PaymentContext = PaymentCommandSupport.MapPaymentContext(
                        correction.Replacement.PaymentContext),
                    correction.Replacement.OccurredAt,
                    correction.Replacement.Comment,
                },
            correction.EntryBatchId,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        NormalizedCorrectPayment correction,
        string fingerprint,
        out Guid primaryEntityId,
        out Guid clientId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == correction.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryId
            && primaryId != Guid.Empty
            && record.RereadTargetId is { } rereadTargetId
            && rereadTargetId != Guid.Empty
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            primaryEntityId = primaryId;
            clientId = rereadTargetId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        primaryEntityId = Guid.Empty;
        clientId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedCorrectPayment correction,
        DateTimeOffset recordedAt,
        Guid primaryEntityId,
        Guid clientId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        var envelope = correction.Envelope;
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
            EntryOrigin = PaymentCommandSupport.MapEntryOrigin(envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = primaryEntityId,
            RereadTargetId = clientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static IReadOnlyList<string> IdentifyChangedFields(
        PaymentRecord original,
        NormalizedPaymentReplacement replacement)
    {
        var changedFields = new List<string>(6);

        if (original.Amount != replacement.Amount.Amount)
        {
            changedFields.Add("amount");
        }

        if (!string.Equals(
                original.Currency,
                replacement.Amount.Currency,
                StringComparison.Ordinal))
        {
            changedFields.Add("currency");
        }

        if (original.OccurredAt.ToUniversalTime() != replacement.OccurredAt)
        {
            changedFields.Add("occurred_at");
        }

        if (!string.Equals(
                original.PaymentContext,
                PaymentCommandSupport.MapPaymentContext(replacement.PaymentContext),
                StringComparison.Ordinal))
        {
            changedFields.Add("payment_context");
        }

        if (original.MembershipId != replacement.MembershipId)
        {
            changedFields.Add("membership_id");
        }

        if (!string.Equals(original.Comment, replacement.Comment, StringComparison.Ordinal))
        {
            changedFields.Add("comment");
        }

        return changedFields.AsReadOnly();
    }

    internal static string SerializeChangedFields(IReadOnlyList<string> changedFields)
    {
        ArgumentNullException.ThrowIfNull(changedFields);
        return JsonSerializer.Serialize(changedFields);
    }

    internal static bool TryMapStoredPaymentContext(
        string value,
        out PaymentContext paymentContext)
    {
        paymentContext = value switch
        {
            "membership_sale" => PaymentContext.MembershipSale,
            "one_off" => PaymentContext.OneOff,
            "trial" => PaymentContext.Trial,
            "negative_closure" => PaymentContext.NegativeClosure,
            "other" => PaymentContext.Other,
            _ => default,
        };

        return paymentContext != default;
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName == IdempotencyUniqueConstraint)
        {
            result = DuplicateSubmission(
                "CorrectPayment with this idempotency key is already in progress or completed.");
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName is CancellationUniqueConstraint
                or CorrectionOriginalUniqueConstraint)
        {
            result = AlreadyProcessed();
            return true;
        }

        if (exception.SqlState is PostgresErrorCodes.SerializationFailure
            or PostgresErrorCodes.DeadlockDetected
            or PostgresErrorCodes.LockNotAvailable)
        {
            result = ConcurrencyConflict();
            return true;
        }

        result = null!;
        return false;
    }

    internal static CommandResult Success(
        PaymentCorrectionMode mode,
        Guid primaryEntityId,
        Guid originalPaymentId,
        Guid? replacementPaymentId,
        Guid clientId,
        AuditEntryId auditEntryId,
        bool changedAfterClose)
    {
        var primaryEntityType = mode == PaymentCorrectionMode.Replace
            ? CorrectPaymentCommand.CorrectionEntityType
            : CorrectPaymentCommand.CancellationEntityType;
        var relatedEntityIds = replacementPaymentId is { } replacementId
            ? new EntityId[]
            {
                new(CorrectPaymentCommand.PaymentEntityType, originalPaymentId),
                new(CorrectPaymentCommand.PaymentEntityType, replacementId),
            }
            :
            [
                new EntityId(
                    CorrectPaymentCommand.PaymentEntityType,
                    originalPaymentId),
            ];

        return CommandResult.Success(
            new EntityId(primaryEntityType, primaryEntityId),
            new EntityId(CorrectPaymentCommand.CanonicalRereadEntityType, clientId),
            relatedEntityIds,
            auditEntryId: auditEntryId,
            changedAfterClose: changedAfterClose);
    }

    internal static CommandResult DuplicateSubmission(string? message = null)
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            message
                ?? "Idempotency key has already been used by a different or incomplete CorrectPayment request.",
            "idempotencyKey");
    }

    internal static CommandResult AlreadyProcessed()
    {
        return Error(
            CommandErrorCode.AlreadyCanceled,
            "Payment has already been canceled or replaced.",
            "originalPaymentId");
    }

    internal static CommandResult ConcurrencyConflict()
    {
        return Error(
            CommandErrorCode.ConcurrencyConflict,
            "Payment correction state changed concurrently. Refresh canonical history and try again.");
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

    private static CommandResult? ValidateAndNormalizeReplacement(
        PaymentReplacement replacement,
        out NormalizedPaymentReplacement? normalizedReplacement)
    {
        normalizedReplacement = null;

        if (replacement.MembershipId == Guid.Empty)
        {
            return ValidationError(
                "Membership id must be a non-empty identifier when supplied.",
                "replacement.membershipId");
        }

        if (replacement.Amount.Amount <= 0)
        {
            return ValidationError(
                "Replacement Payment amount must be greater than zero.",
                "replacement.amount");
        }

        Money amount;
        try
        {
            amount = new Money(
                replacement.Amount.Amount,
                replacement.Amount.Currency);
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, "replacement.amount");
        }

        if (!Enum.IsDefined(replacement.PaymentContext))
        {
            return ValidationError(
                "Replacement Payment context is not supported.",
                "replacement.paymentContext");
        }

        if (replacement.PaymentContext == PaymentContext.NegativeClosure)
        {
            return ValidationError(
                "Negative closure requires its explicit Membership workflow and cannot be introduced by Payment correction.",
                "replacement.paymentContext");
        }

        if (replacement.OccurredAt == default)
        {
            return ValidationError(
                "Replacement Payment occurred_at is required.",
                "replacement.occurredAt");
        }

        var comment = NormalizeOptional(replacement.Comment);
        if (comment?.Length > CommentMaxLength)
        {
            return ValidationError(
                $"Replacement Payment comment must be {CommentMaxLength} characters or fewer.",
                "replacement.comment");
        }

        normalizedReplacement = new NormalizedPaymentReplacement(
            replacement.MembershipId,
            amount,
            replacement.PaymentContext,
            replacement.OccurredAt.ToUniversalTime(),
            comment);
        return null;
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
                "Occurred_at is required for Payment correction.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Reason is required for Payment correction.",
                "reason");
        }

        if (reason.Length > ReasonMaxLength)
        {
            return ValidationError(
                $"Reason must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
        if (comment?.Length > CommentMaxLength)
        {
            return ValidationError(
                $"Correction comment must be {CommentMaxLength} characters or fewer.",
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

internal sealed record NormalizedCorrectPayment(
    Guid OriginalPaymentId,
    PaymentCorrectionMode Mode,
    NormalizedPaymentReplacement? Replacement,
    Guid? EntryBatchId,
    CommandEnvelope Envelope);

internal sealed record NormalizedPaymentReplacement(
    Guid? MembershipId,
    Money Amount,
    PaymentContext PaymentContext,
    DateTimeOffset OccurredAt,
    string? Comment);
