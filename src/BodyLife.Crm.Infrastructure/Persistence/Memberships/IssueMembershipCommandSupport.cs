using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class IssueMembershipCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 2000;
    private const int PaymentCommentMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static CommandResult? ValidateAndNormalize(
        IssueMembershipCommand command,
        out NormalizedMembershipIssue? normalizedIssue)
    {
        normalizedIssue = null;

        if (command.ClientId == Guid.Empty)
        {
            return ValidationError("Client id is required.", "clientId");
        }

        if (command.MembershipTypeId == Guid.Empty)
        {
            return ValidationError("Membership type id is required.", "membershipTypeId");
        }

        if (command.StartDate == default)
        {
            return ValidationError("Start date is required.", "startDate");
        }

        if (command.NegativeHandlingDecision is { } decision
            && !Enum.IsDefined(decision))
        {
            return ValidationError(
                "Negative handling decision is not supported.",
                "negativeHandlingDecision");
        }

        if (command.EntryBatchId is not null)
        {
            return ValidationError(
                "Entry batch is not supported by the ordinary membership issue workflow.",
                "entryBatchId");
        }

        var paymentValidation = ValidateAndNormalizePayment(
            command.Payment,
            out var normalizedPayment);
        if (paymentValidation is not null)
        {
            return paymentValidation;
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var normalizedEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        if (normalizedPayment is not null
            && normalizedEnvelope!.Comment?.Length > PaymentCommentMaxLength)
        {
            return ValidationError(
                $"Issue comment must be {PaymentCommentMaxLength} characters or fewer when a Payment is included.",
                "envelope.comment");
        }

        normalizedIssue = new NormalizedMembershipIssue(
            command.ClientId,
            command.MembershipTypeId,
            command.StartDate,
            command.NegativeHandlingDecision,
            normalizedPayment,
            normalizedEnvelope!);
        return null;
    }

    internal static string CreateFingerprint(NormalizedMembershipIssue issue)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = issue.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(issue.Envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(issue.Envelope.Actor.AccountKind),
            ActorSessionId = issue.Envelope.Actor.SessionId.Value,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(issue.Envelope.EntryOrigin),
            issue.Envelope.OccurredAt,
            EnvelopeReason = issue.Envelope.Reason,
            EnvelopeComment = issue.Envelope.Comment,
            issue.ClientId,
            issue.MembershipTypeId,
            issue.StartDate,
            NegativeHandlingDecision = MapNegativeHandlingDecision(
                issue.NegativeHandlingDecision),
            Payment = issue.Payment is null
                ? null
                : new
                {
                    Amount = issue.Payment.Amount.Amount,
                    Currency = issue.Payment.Amount.Currency,
                    PaymentContext = "membership_sale",
                },
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static CommandResult ReplayOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedMembershipIssue issue,
        Guid actorAccountId,
        string fingerprint)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == actorAccountId
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId.HasValue
            && record.PrimaryEntityId.Value != Guid.Empty
            && record.RereadTargetId == issue.ClientId
            && record.AuditEntryId.HasValue)
        {
            return Success(
                record.PrimaryEntityId.Value,
                issue.ClientId,
                new AuditEntryId(record.AuditEntryId.Value),
                WarningCodes(issue.NegativeHandlingDecision));
        }

        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete membership issue request.",
            "idempotencyKey");
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedMembershipIssue issue,
        DateTimeOffset recordedAt,
        Guid membershipId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = issue.Envelope.IdempotencyKey!,
            RequestCorrelationId = issue.Envelope.RequestCorrelationId.Value!,
            AccountId = issue.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(issue.Envelope.Actor.Role),
            AccountKind = MapAccountKind(issue.Envelope.Actor.AccountKind),
            SessionId = issue.Envelope.Actor.SessionId.Value,
            DeviceLabel = issue.Envelope.Actor.DeviceLabel,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(issue.Envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = membershipId,
            RereadTargetId = issue.ClientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static CommandResult Success(
        Guid membershipId,
        Guid clientId,
        AuditEntryId auditEntryId,
        IReadOnlyList<string> warningCodes)
    {
        return CommandResult.Success(
            new EntityId(IssueMembershipCommand.PrimaryEntityType, membershipId),
            new EntityId(IssueMembershipCommand.CanonicalRereadEntityType, clientId),
            warnings: warningCodes,
            auditEntryId: auditEntryId);
    }

    internal static IReadOnlyList<string> WarningCodes(
        MembershipNegativeHandlingDecision? decision)
    {
        return decision == MembershipNegativeHandlingDecision.LeaveVisible
            ? [MembershipWarningCodes.NegativeBalance]
            : [];
    }

    internal static string? MapNegativeHandlingDecision(
        MembershipNegativeHandlingDecision? decision)
    {
        return decision switch
        {
            null => null,
            MembershipNegativeHandlingDecision.LeaveVisible => "leave_visible",
            MembershipNegativeHandlingDecision.CoverWithNewMembership =>
                "cover_with_new_membership",
            MembershipNegativeHandlingDecision.RecordExplicitClosure =>
                "record_explicit_closure",
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
        };
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
        return MembershipCommandSupport.Error(code, message, field);
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

        if (envelope.EntryOrigin != EntryOrigin.Normal)
        {
            return ValidationError(
                "This membership issue workflow accepts only normal entry origin.",
                "entryOrigin");
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

    private static CommandResult? ValidateAndNormalizePayment(
        MembershipIssuePayment? payment,
        out MembershipIssuePayment? normalizedPayment)
    {
        normalizedPayment = null;
        if (payment is null)
        {
            return null;
        }

        if (!Enum.IsDefined(payment.PaymentContext)
            || payment.PaymentContext != PaymentContext.MembershipSale)
        {
            return ValidationError(
                "IssueMembership accepts only membership-sale Payment context.",
                "payment.paymentContext");
        }

        if (payment.Amount.Amount <= 0)
        {
            return ValidationError(
                "Payment amount must be greater than zero.",
                "payment.amount");
        }

        try
        {
            normalizedPayment = payment with
            {
                Amount = new Money(
                    payment.Amount.Amount,
                    payment.Amount.Currency),
            };
        }
        catch (ArgumentException exception)
        {
            return ValidationError(exception.Message, "payment.amount");
        }

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

internal sealed record NormalizedMembershipIssue(
    Guid ClientId,
    Guid MembershipTypeId,
    DateOnly StartDate,
    MembershipNegativeHandlingDecision? NegativeHandlingDecision,
    MembershipIssuePayment? Payment,
    CommandEnvelope Envelope)
{
    public string IdempotencyKey => Envelope.IdempotencyKey!;
}
