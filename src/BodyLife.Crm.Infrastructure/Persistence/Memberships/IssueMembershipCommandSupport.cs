using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class IssueMembershipCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int AuditReasonMaxLength = 1000;
    private const int AuditCommentMaxLength = 1000;
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

        if (command.ExpectedMembershipTypeUpdatedAt == default)
        {
            return ValidationError(
                "Expected membership type version is required.",
                "expectedMembershipTypeUpdatedAt");
        }

        if (command.StartDate == default)
        {
            return ValidationError("Start date is required.", "startDate");
        }

        if (string.IsNullOrWhiteSpace(command.PreviewToken))
        {
            return ValidationError(
                "A signed membership issue preview is required.",
                "previewToken");
        }

        if (command.EntryBatchId is not null)
        {
            return ValidationError(
                "Normal membership issue cannot carry paper batch metadata.",
                "entryBatchId");
        }

        var envelopeValidation = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var normalizedEnvelope);
        if (envelopeValidation is not null)
        {
            return envelopeValidation;
        }

        normalizedIssue = new NormalizedMembershipIssue(
            command.ClientId,
            command.MembershipTypeId,
            command.ExpectedMembershipTypeUpdatedAt.ToUniversalTime(),
            command.StartDate,
            command.PreviewToken.Trim(),
            command.EntryBatchId,
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
            issue.Envelope.EntryBatchRowId,
            issue.ClientId,
            issue.MembershipTypeId,
            issue.ExpectedMembershipTypeUpdatedAt,
            issue.StartDate,
            issue.EntryBatchId,
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
                []);
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

        if (envelope.EntryOrigin is not EntryOrigin.Normal and not EntryOrigin.PaperFallback)
        {
            return ValidationError(
                "This membership issue workflow accepts only normal or paper fallback entry origin.",
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

        if (envelope.EntryOrigin == EntryOrigin.Normal
            && envelope.EntryBatchRowId is not null)
        {
            return ValidationError(
                "Normal membership issue cannot carry paper row metadata.",
                "entryBatchRowId");
        }

        if (envelope.EntryOrigin == EntryOrigin.PaperFallback
            && (envelope.EntryBatchRowId is not { } entryBatchRowId
                || entryBatchRowId == Guid.Empty))
        {
            return ValidationError(
                "Paper fallback membership issue requires an entry batch row id.",
                "entryBatchRowId");
        }

        if (envelope.EntryOrigin == EntryOrigin.PaperFallback
            && reason is null
            && comment is null)
        {
            return ValidationError(
                "Paper fallback membership issue requires a reason or comment.",
                "reason");
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

        if (envelope.EntryOrigin == EntryOrigin.PaperFallback && occurredAt is null)
        {
            return ValidationError(
                "Paper fallback membership issue requires occurred_at.",
                "occurredAt");
        }

        normalizedEnvelope = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(requestCorrelationId),
            envelope.EntryOrigin,
            occurredAt,
            idempotencyKey,
            reason,
            comment,
            envelope.EntryBatchRowId);
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
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    DateOnly StartDate,
    string PreviewToken,
    Guid? EntryBatchId,
    CommandEnvelope Envelope)
{
    public string IdempotencyKey => Envelope.IdempotencyKey!;
}
