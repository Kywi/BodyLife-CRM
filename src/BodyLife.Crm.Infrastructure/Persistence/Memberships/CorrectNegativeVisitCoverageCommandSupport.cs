using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class CorrectNegativeVisitCoverageCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string CorrectionOriginalConstraint =
        "ux_negative_closure_corrections_original";
    private const string CorrectionIdempotencyConstraint =
        "ux_negative_closure_corrections_idempotency_key";
    private const string ClosureIdempotencyConstraint =
        "ux_negative_closures_idempotency_key";
    private const string ActiveVisitConstraint =
        "ux_negative_closure_items_active_visit";
    private const int MaximumLines = 50;
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static CommandResult? ValidateAndNormalize(
        CorrectNegativeVisitCoverageCommand command,
        out NormalizedNegativeCoverageCorrection? normalized)
    {
        normalized = null;
        if (command.OriginalNegativeClosureId == Guid.Empty)
        {
            return ValidationError(
                "Original negative closure id is required.",
                "originalNegativeClosureId");
        }

        if (!Enum.IsDefined(command.Mode))
        {
            return ValidationError("Correction mode is not supported.", "mode");
        }

        var normalizedLinesResult = NormalizeReplacementLines(
            command.ReplacementOneOffLines,
            out var normalizedLines);
        if (normalizedLinesResult is not null)
        {
            return normalizedLinesResult;
        }

        if (command.Mode == NegativeVisitCoverageCorrectionMode.Cancel)
        {
            if (normalizedLines!.Count > 0
                || command.ReplacementNewMembershipCoverageCount is not null
                || command.ExpectedOldestOpenNegativeVisitId is not null)
            {
                return ValidationError(
                    "Cancellation cannot carry replacement selections.",
                    "mode");
            }
        }
        else
        {
            var hasOneOffReplacement = normalizedLines!.Count > 0;
            var hasNewMembershipReplacement =
                command.ReplacementNewMembershipCoverageCount is > 0;
            if (hasOneOffReplacement == hasNewMembershipReplacement)
            {
                return ValidationError(
                    "Replacement requires exactly one same-method replacement selection.",
                    "replacement");
            }

            if (command.ReplacementNewMembershipCoverageCount is <= 0)
            {
                return ValidationError(
                    "New-Membership replacement coverage count must be positive.",
                    "replacementNewMembershipCoverageCount");
            }

            if (!command.ExpectedOldestOpenNegativeVisitId.HasValue
                || command.ExpectedOldestOpenNegativeVisitId.Value == Guid.Empty)
            {
                return ValidationError(
                    "Expected oldest open negative Visit id is required for replacement.",
                    "expectedOldestOpenNegativeVisitId");
            }
        }

        if (command.EntryBatchId is not null)
        {
            return ValidationError(
                "Coverage correction derives paper batch metadata from its paper row.",
                "entryBatchId");
        }

        var envelopeResult = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeResult is not null)
        {
            return envelopeResult;
        }

        normalized = new NormalizedNegativeCoverageCorrection(
            command.OriginalNegativeClosureId,
            command.Mode,
            normalizedLines!,
            command.ReplacementNewMembershipCoverageCount,
            command.ExpectedOldestOpenNegativeVisitId,
            command.EntryBatchId,
            envelope!);
        return null;
    }

    internal static string CreateFingerprint(
        NormalizedNegativeCoverageCorrection correction)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = correction.Envelope.Actor.AccountId.Value,
            ActorRole = correction.Envelope.Actor.Role.ToString(),
            ActorAccountKind = correction.Envelope.Actor.AccountKind.ToString(),
            ActorSessionId = correction.Envelope.Actor.SessionId.Value,
            correction.Envelope.EntryOrigin,
            correction.Envelope.OccurredAt,
            correction.Envelope.Reason,
            correction.Envelope.Comment,
            correction.OriginalNegativeClosureId,
            correction.Mode,
            ReplacementOneOffLines = correction.ReplacementOneOffLines.Select(line => new
            {
                line.MembershipTypeId,
                line.ExpectedMembershipTypeUpdatedAt,
                line.Quantity,
                line.Sequence,
            }),
            correction.ReplacementNewMembershipCoverageCount,
            correction.ExpectedOldestOpenNegativeVisitId,
            correction.Envelope.EntryBatchRowId,
        });
        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        NormalizedNegativeCoverageCorrection correction,
        string fingerprint,
        out Guid correctionId,
        out Guid clientId,
        out AuditEntryId auditEntryId)
    {
        correctionId = default;
        clientId = default;
        auditEntryId = default;
        if (record.Status != SucceededIdempotencyStatus
            || record.AccountId != correction.Envelope.Actor.AccountId.Value
            || !string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            || record.PrimaryEntityId is not { } storedCorrectionId
            || storedCorrectionId == Guid.Empty
            || record.RereadTargetId is not { } storedClientId
            || storedClientId == Guid.Empty
            || record.AuditEntryId is not { } storedAuditId
            || storedAuditId == Guid.Empty)
        {
            return false;
        }

        correctionId = storedCorrectionId;
        clientId = storedClientId;
        auditEntryId = new AuditEntryId(storedAuditId);
        return true;
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedNegativeCoverageCorrection correction,
        DateTimeOffset recordedAt,
        Guid correctionId,
        Guid clientId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = correction.IdempotencyKey,
            RequestCorrelationId = correction.Envelope.RequestCorrelationId.Value!,
            AccountId = correction.Envelope.Actor.AccountId.Value,
            ActorRole = MapRole(correction.Envelope.Actor.Role),
            AccountKind = MapKind(correction.Envelope.Actor.AccountKind),
            SessionId = correction.Envelope.Actor.SessionId.Value,
            DeviceLabel = correction.Envelope.Actor.DeviceLabel,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                correction.Envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = correctionId,
            RereadTargetId = clientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static CommandResult Success(
        Guid correctionId,
        Guid originalClosureId,
        Guid? replacementClosureId,
        Guid clientId,
        IReadOnlyCollection<Guid> membershipIds,
        Guid? originalPaymentId,
        Guid? replacementPaymentId,
        AuditEntryId auditEntryId,
        int remainingNegativeBalance,
        bool changedAfterClose)
    {
        var related = membershipIds
            .Order()
            .Select(id => new EntityId(MembershipAuditActions.MembershipEntityType, id))
            .Append(new EntityId(
                CorrectNegativeVisitCoverageCommand.ClosureEntityType,
                originalClosureId))
            .Concat(replacementClosureId is { } replacementId
                ? [new EntityId(
                    CorrectNegativeVisitCoverageCommand.ClosureEntityType,
                    replacementId)]
                : [])
            .Concat(originalPaymentId is { } paymentId
                ? [new EntityId("payment", paymentId)]
                : [])
            .Concat(replacementPaymentId is { } replacementPayment
                ? [new EntityId("payment", replacementPayment)]
                : [])
            .ToArray();
        var warnings = remainingNegativeBalance > 0
            ? new[] { MembershipWarningCodes.NegativeBalance }
            : [];
        return CommandResult.Success(
            new EntityId(
                CorrectNegativeVisitCoverageCommand.PrimaryEntityType,
                correctionId),
            new EntityId(
                CorrectNegativeVisitCoverageCommand.CanonicalRereadEntityType,
                clientId),
            related,
            warnings,
            auditEntryId,
            changedAfterClose);
    }

    internal static bool TryMapPostgresFailure(
        PostgresException exception,
        out CommandResult result)
    {
        if (MembershipCommandSupport.TryMapPostgresFailure(exception, out result))
        {
            return true;
        }

        if (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            result = exception.ConstraintName switch
            {
                CorrectionOriginalConstraint => Error(
                    CommandErrorCode.AlreadyCanceled,
                    "Negative coverage has already been corrected.",
                    "originalNegativeClosureId"),
                CorrectionIdempotencyConstraint or ClosureIdempotencyConstraint => Error(
                    CommandErrorCode.DuplicateSubmission,
                    "Coverage correction idempotency key has already been used.",
                    "idempotencyKey"),
                ActiveVisitConstraint => Error(
                    CommandErrorCode.StaleState,
                    "A replacement Visit was covered concurrently. Refresh canonical state.",
                    "expectedOldestOpenNegativeVisitId"),
                _ => null!,
            };
            return result is not null;
        }

        result = null!;
        return false;
    }

    internal static CommandResult DuplicateSubmission()
    {
        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete coverage correction.",
            "idempotencyKey");
    }

    internal static CommandResult ValidationError(string message, string? field = null)
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

    private static CommandResult? NormalizeReplacementLines(
        IReadOnlyList<NegativeVisitClosureLineSelection>? lines,
        out IReadOnlyList<NormalizedOneOffNegativeClosureLine>? normalized)
    {
        normalized = [];
        if (lines is null || lines.Count == 0)
        {
            return null;
        }

        if (lines.Count > MaximumLines)
        {
            return ValidationError(
                $"At most {MaximumLines} replacement lines are supported.",
                "replacementOneOffLines");
        }

        var items = new List<NormalizedOneOffNegativeClosureLine>(lines.Count);
        var typeIds = new HashSet<Guid>();
        long total = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line is null)
            {
                return ValidationError(
                    "Replacement lines cannot contain a missing item.",
                    $"replacementOneOffLines[{index}]");
            }

            if (line.MembershipTypeId == Guid.Empty
                || !typeIds.Add(line.MembershipTypeId))
            {
                return ValidationError(
                    "Every replacement one-off type must be non-empty and unique.",
                    $"replacementOneOffLines[{index}].membershipTypeId");
            }

            if (line.ExpectedMembershipTypeUpdatedAt == default)
            {
                return ValidationError(
                    "Expected one-off membership type version is required.",
                    $"replacementOneOffLines[{index}].expectedMembershipTypeUpdatedAt");
            }

            if (line.Quantity <= 0)
            {
                return ValidationError(
                    "Replacement quantity must be positive.",
                    $"replacementOneOffLines[{index}].quantity");
            }

            total += line.Quantity;
            if (total > int.MaxValue)
            {
                return ValidationError(
                    "Replacement quantity exceeds the supported range.",
                    "replacementOneOffLines");
            }

            items.Add(new NormalizedOneOffNegativeClosureLine(
                line.MembershipTypeId,
                line.ExpectedMembershipTypeUpdatedAt.ToUniversalTime(),
                line.Quantity,
                index + 1));
        }

        normalized = items.AsReadOnly();
        return null;
    }

    private static CommandResult? ValidateAndNormalizeEnvelope(
        CommandEnvelope envelope,
        out CommandEnvelope? normalized)
    {
        normalized = null;
        if (envelope?.Actor is null)
        {
            return Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to correct negative coverage.");
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

        if (envelope.EntryOrigin is not (EntryOrigin.Normal or EntryOrigin.PaperFallback))
        {
            return ValidationError(
                "Coverage correction accepts only normal or paper fallback entry origin.",
                "entryOrigin");
        }

        if (envelope.EntryOrigin == EntryOrigin.Normal
            && envelope.EntryBatchRowId is not null)
        {
            return ValidationError(
                "Entry batch row id is only valid for paper fallback entry.",
                "entryBatchRowId");
        }

        if (envelope.EntryOrigin == EntryOrigin.PaperFallback
            && (envelope.EntryBatchRowId is null || envelope.EntryBatchRowId == Guid.Empty))
        {
            return ValidationError(
                "Paper fallback entry requires an entry batch row id.",
                "entryBatchRowId");
        }

        if (envelope.OccurredAt is null
            || !BusinessTimeZone.TryNormalizeUtcInstant(
                envelope.OccurredAt.Value,
                out var occurredAt))
        {
            return ValidationError(
                "A supported occurred_at is required for coverage correction.",
                "occurredAt");
        }

        var reason = NormalizeOptional(envelope.Reason);
        if (reason is null)
        {
            return Error(
                CommandErrorCode.ReasonRequired,
                "Reason is required for coverage correction.",
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

    private static string MapRole(ActorRole role) => role switch
    {
        ActorRole.Owner => "owner",
        ActorRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    private static string MapKind(AccountKind kind) => kind switch
    {
        AccountKind.Owner => "owner",
        AccountKind.NamedAdmin => "named_admin",
        AccountKind.SharedReceptionAdmin => "shared_reception_admin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
}

internal sealed record NormalizedNegativeCoverageCorrection(
    Guid OriginalNegativeClosureId,
    NegativeVisitCoverageCorrectionMode Mode,
    IReadOnlyList<NormalizedOneOffNegativeClosureLine> ReplacementOneOffLines,
    int? ReplacementNewMembershipCoverageCount,
    Guid? ExpectedOldestOpenNegativeVisitId,
    Guid? EntryBatchId,
    CommandEnvelope Envelope)
{
    public string IdempotencyKey => Envelope.IdempotencyKey!;
}
