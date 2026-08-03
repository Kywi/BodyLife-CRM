using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class IssuedMembershipSaleCorrectionCommandSupport
{
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 2000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static CommandResult? ValidateAndNormalize(
        ReplaceIssuedMembershipCommand command,
        out NormalizedIssuedMembershipSaleCorrection? normalized)
    {
        normalized = null;
        if (command.OriginalMembershipId == Guid.Empty)
        {
            return ValidationError(
                "Original Membership id is required.",
                "originalMembershipId");
        }

        if (command.ReplacementMembershipTypeId == Guid.Empty)
        {
            return ValidationError(
                "Replacement Membership type id is required.",
                "replacementMembershipTypeId");
        }

        if (command.ExpectedMembershipTypeUpdatedAt == default)
        {
            return ValidationError(
                "Expected Membership type version is required.",
                "expectedMembershipTypeUpdatedAt");
        }

        if (command.ReplacementStartDate == default)
        {
            return ValidationError(
                "Replacement start date is required.",
                "replacementStartDate");
        }

        var envelopeResult = ValidateEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeResult is not null)
        {
            return envelopeResult;
        }

        var suppliedToken = command.ExpectedDependencyToken?.Trim();
        if (string.IsNullOrWhiteSpace(suppliedToken)
            || suppliedToken.Length > 128)
        {
            return ValidationError(
                "Expected dependency token is required and must be 128 characters or fewer.",
                "expectedDependencyToken");
        }

        normalized = new NormalizedIssuedMembershipSaleCorrection(
            IssuedMembershipSaleCorrectionMode.Replace,
            command.OriginalMembershipId,
            command.ReplacementMembershipTypeId,
            command.ExpectedMembershipTypeUpdatedAt,
            command.ReplacementStartDate,
            suppliedToken,
            envelope!);
        return null;
    }

    internal static CommandResult? ValidateAndNormalize(
        CancelIssuedMembershipSaleCommand command,
        out NormalizedIssuedMembershipSaleCorrection? normalized)
    {
        normalized = null;
        if (command.OriginalMembershipId == Guid.Empty)
        {
            return ValidationError(
                "Original Membership id is required.",
                "originalMembershipId");
        }

        var envelopeResult = ValidateEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeResult is not null)
        {
            return envelopeResult;
        }

        var suppliedToken = command.ExpectedDependencyToken?.Trim();
        if (string.IsNullOrWhiteSpace(suppliedToken)
            || suppliedToken.Length > 128)
        {
            return ValidationError(
                "Expected dependency token is required and must be 128 characters or fewer.",
                "expectedDependencyToken");
        }

        normalized = new NormalizedIssuedMembershipSaleCorrection(
            IssuedMembershipSaleCorrectionMode.Cancel,
            command.OriginalMembershipId,
            null,
            null,
            null,
            suppliedToken,
            envelope!);
        return null;
    }

    internal static string CreateFingerprint(
        NormalizedIssuedMembershipSaleCorrection correction)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Mode = correction.Mode.ToString(),
            correction.OriginalMembershipId,
            correction.ReplacementMembershipTypeId,
            correction.ExpectedMembershipTypeUpdatedAt,
            correction.ReplacementStartDate,
            correction.ExpectedDependencyToken,
            ActorAccountId = correction.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(correction.Envelope.Actor.Role),
            AccountKind = MapAccountKind(correction.Envelope.Actor.AccountKind),
            ActorSessionId = correction.Envelope.Actor.SessionId.Value,
            correction.Envelope.Actor.DeviceLabel,
            RequestCorrelationId = correction.Envelope.RequestCorrelationId.Value,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                correction.Envelope.EntryOrigin),
            correction.Envelope.OccurredAt,
            correction.Envelope.Reason,
            correction.Envelope.Comment,
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        BodyLifeDbContext dbContext,
        CommandIdempotencyRecord record,
        NormalizedIssuedMembershipSaleCorrection correction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (record.Status != "succeeded"
            || record.AccountId != correction.Envelope.Actor.AccountId.Value
            || !string.Equals(
                record.ResultFingerprint,
                fingerprint,
                StringComparison.Ordinal)
            || record.PrimaryEntityId is not { } correctionId
            || correctionId == Guid.Empty
            || record.RereadTargetId is not { } clientId
            || clientId == Guid.Empty
            || record.AuditEntryId is not { } auditId
            || auditId == Guid.Empty)
        {
            return DuplicateError();
        }

        var source = await dbContext.Set<IssuedMembershipSaleCorrectionRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == correctionId, cancellationToken);
        if (source is null
            || source.ClientId != clientId
            || source.OriginalMembershipId != correction.OriginalMembershipId
            || source.CorrectionMode != MapMode(correction.Mode))
        {
            return DuplicateError();
        }

        var expectedAction = correction.Mode
            == IssuedMembershipSaleCorrectionMode.Replace
                ? MembershipAuditActions.Replaced
                : MembershipAuditActions.SaleCanceled;
        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.Id == auditId
                    && entry.ActionType == expectedAction
                    && entry.EntityType == MembershipAuditActions.MembershipEntityType
                    && entry.EntityId == correction.OriginalMembershipId,
                cancellationToken);
        if (audit is null)
        {
            return DuplicateError();
        }

        return Success(
            source,
            new AuditEntryId(auditId),
            audit.ChangedAfterClose);
    }

    internal static CommandIdempotencyRecord CreateIdempotencyRecord(
        string commandName,
        NormalizedIssuedMembershipSaleCorrection correction,
        DateTimeOffset recordedAt,
        IssuedMembershipSaleCorrectionRecord source,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = correction.Envelope.IdempotencyKey!,
            RequestCorrelationId = correction.Envelope.RequestCorrelationId.Value!,
            AccountId = correction.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(correction.Envelope.Actor.Role),
            AccountKind = MapAccountKind(correction.Envelope.Actor.AccountKind),
            SessionId = correction.Envelope.Actor.SessionId.Value,
            DeviceLabel = correction.Envelope.Actor.DeviceLabel,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                correction.Envelope.EntryOrigin),
            Status = "succeeded",
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = source.Id,
            RereadTargetId = source.ClientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static CommandResult Success(
        IssuedMembershipSaleCorrectionRecord source,
        AuditEntryId auditEntryId,
        bool changedAfterClose = false)
    {
        var related = new List<EntityId>
        {
            new("membership", source.OriginalMembershipId),
            new("payment", source.OriginalPaymentId),
        };
        if (source.ReplacementMembershipId is { } replacementMembershipId)
        {
            related.Add(new EntityId("membership", replacementMembershipId));
        }

        if (source.ReplacementPaymentId is { } replacementPaymentId)
        {
            related.Add(new EntityId("payment", replacementPaymentId));
        }

        return CommandResult.Success(
            new EntityId(
                ReplaceIssuedMembershipCommand.PrimaryEntityType,
                source.Id),
            new EntityId(
                ReplaceIssuedMembershipCommand.CanonicalRereadEntityType,
                source.ClientId),
            related,
            auditEntryId: auditEntryId,
            changedAfterClose: changedAfterClose);
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (MembershipCommandSupport.TryMapPostgresFailure(exception, out result))
        {
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation
            && exception.ConstraintName is
                "ux_issued_sale_corrections_original_membership"
                or "ux_issued_sale_corrections_original_payment"
                or "ux_issued_sale_corrections_replacement_membership"
                or "ux_issued_sale_corrections_replacement_payment")
        {
            result = Error(
                CommandErrorCode.StaleState,
                "The Membership sale was already changed. Refresh canonical state.",
                "originalMembershipId");
            return true;
        }

        result = null!;
        return false;
    }

    internal static string MapMode(IssuedMembershipSaleCorrectionMode mode)
    {
        return mode switch
        {
            IssuedMembershipSaleCorrectionMode.Cancel => "cancel",
            IssuedMembershipSaleCorrectionMode.Replace => "replace",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
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
        return CommandResult.Error([new CommandError(code, message, field)]);
    }

    private static CommandResult? ValidateEnvelope(
        CommandEnvelope? envelope,
        out CommandEnvelope? normalized)
    {
        normalized = null;
        if (envelope?.Actor is null
            || !MembershipCommandSupport.IsAllowedActorShape(envelope.Actor))
        {
            return Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner, named Admin or shared Reception/Admin session is required.");
        }

        var idempotencyKey = envelope.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey)
            || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            return ValidationError(
                $"Idempotency key is required and must be {IdempotencyKeyMaxLength} characters or fewer.",
                "idempotencyKey");
        }

        var correlationId = envelope.RequestCorrelationId.Value?.Trim();
        if (string.IsNullOrWhiteSpace(correlationId)
            || correlationId.Length > CorrelationIdMaxLength)
        {
            return ValidationError(
                $"Request correlation id is required and must be {CorrelationIdMaxLength} characters or fewer.",
                "requestCorrelationId");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null || reason.Length > ReasonMaxLength)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                $"Reason is required and must be {ReasonMaxLength} characters or fewer.",
                "reason");
        }

        var comment = NormalizeOptional(envelope.Comment);
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

        if (envelope.EntryOrigin != EntryOrigin.Normal)
        {
            return ValidationError(
                "Issued-sale correction currently requires normal entry origin.",
                "entryOrigin");
        }

        if (envelope.OccurredAt is not { } occurredAt
            || !BusinessTimeZone.TryNormalizeUtcInstant(
                occurredAt,
                out var normalizedOccurredAt))
        {
            return ValidationError(
                "Occurred_at is required and must be a supported instant.",
                "occurredAt");
        }

        normalized = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(correlationId),
            envelope.EntryOrigin,
            normalizedOccurredAt,
            idempotencyKey,
            reason,
            comment);
        return null;
    }

    private static CommandResult DuplicateError()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete issued-sale correction.",
            "idempotencyKey");
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

internal enum IssuedMembershipSaleCorrectionMode
{
    Cancel = 1,
    Replace,
}

internal sealed record NormalizedIssuedMembershipSaleCorrection(
    IssuedMembershipSaleCorrectionMode Mode,
    Guid OriginalMembershipId,
    Guid? ReplacementMembershipTypeId,
    DateTimeOffset? ExpectedMembershipTypeUpdatedAt,
    DateOnly? ReplacementStartDate,
    string ExpectedDependencyToken,
    CommandEnvelope Envelope)
{
    internal string IdempotencyKey => Envelope.IdempotencyKey!;
}
