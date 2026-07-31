using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;
using Npgsql;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal static class NegativeCoverageCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private const string ActiveVisitConstraint =
        "ux_negative_closure_items_active_visit";
    private const string ClosureIdempotencyConstraint =
        "ux_negative_closures_idempotency_key";
    private const int MaximumLines = 50;
    private const int IdempotencyKeyMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;
    private const int DeviceLabelMaxLength = 120;
    private const int ReasonMaxLength = 1000;
    private const int CommentMaxLength = 1000;
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static CommandResult? ValidateAndNormalize(
        CloseNegativeVisitsOneOffCommand command,
        out NormalizedOneOffNegativeClosure? normalized)
    {
        normalized = null;

        if (command.ClientId == Guid.Empty)
        {
            return ValidationError("Client id is required.", "clientId");
        }

        if (command.ExpectedOldestOpenNegativeVisitId == Guid.Empty)
        {
            return ValidationError(
                "Expected oldest open negative Visit id is required.",
                "expectedOldestOpenNegativeVisitId");
        }

        if (command.Lines is null
            || command.Lines.Count == 0
            || command.Lines.Count > MaximumLines)
        {
            return ValidationError(
                $"One to {MaximumLines} one-off closure lines are required.",
                "lines");
        }

        var normalizedLines = new List<NormalizedOneOffNegativeClosureLine>(
            command.Lines.Count);
        var typeIds = new HashSet<Guid>();
        long visitsCount = 0;
        for (var index = 0; index < command.Lines.Count; index++)
        {
            var line = command.Lines[index];
            if (line is null)
            {
                return ValidationError(
                    "Closure lines cannot contain a missing item.",
                    $"lines[{index}]");
            }

            if (line.MembershipTypeId == Guid.Empty)
            {
                return ValidationError(
                    "Membership type id is required for every closure line.",
                    $"lines[{index}].membershipTypeId");
            }

            if (!typeIds.Add(line.MembershipTypeId))
            {
                return ValidationError(
                    "Each one-off membership type can appear only once.",
                    $"lines[{index}].membershipTypeId");
            }

            if (line.ExpectedMembershipTypeUpdatedAt == default)
            {
                return ValidationError(
                    "Expected membership type version is required.",
                    $"lines[{index}].expectedMembershipTypeUpdatedAt");
            }

            if (line.Quantity <= 0)
            {
                return ValidationError(
                    "Closure line quantity must be positive.",
                    $"lines[{index}].quantity");
            }

            visitsCount += line.Quantity;
            if (visitsCount > int.MaxValue)
            {
                return ValidationError(
                    "Total closure quantity exceeds the supported range.",
                    "lines");
            }

            normalizedLines.Add(new NormalizedOneOffNegativeClosureLine(
                line.MembershipTypeId,
                line.ExpectedMembershipTypeUpdatedAt.ToUniversalTime(),
                line.Quantity,
                index + 1));
        }

        if (command.EntryBatchId is not null)
        {
            return ValidationError(
                "Normal one-off closure cannot carry paper batch metadata.",
                "entryBatchId");
        }

        var envelopeError = ValidateAndNormalizeEnvelope(
            command.Envelope,
            out var envelope);
        if (envelopeError is not null)
        {
            return envelopeError;
        }

        normalized = new NormalizedOneOffNegativeClosure(
            command.ClientId,
            command.ExpectedOldestOpenNegativeVisitId,
            normalizedLines.AsReadOnly(),
            (int)visitsCount,
            command.EntryBatchId,
            envelope!);
        return null;
    }

    internal static string CreateFingerprint(NormalizedOneOffNegativeClosure closure)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = closure.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(closure.Envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(closure.Envelope.Actor.AccountKind),
            ActorSessionId = closure.Envelope.Actor.SessionId.Value,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                closure.Envelope.EntryOrigin),
            closure.Envelope.OccurredAt,
            EnvelopeReason = closure.Envelope.Reason,
            EnvelopeComment = closure.Envelope.Comment,
            closure.ClientId,
            closure.ExpectedOldestOpenNegativeVisitId,
            Lines = closure.Lines.Select(line => new
            {
                line.MembershipTypeId,
                line.ExpectedMembershipTypeUpdatedAt,
                line.Quantity,
                line.Sequence,
            }),
            closure.EntryBatchId,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static CommandResult ReplayOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedOneOffNegativeClosure closure,
        string fingerprint)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == closure.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryEntityId
            && primaryEntityId != Guid.Empty
            && record.RereadTargetId == closure.ClientId
            && record.AuditEntryId is { } auditEntryId)
        {
            return CommandResult.Success(
                new EntityId(
                    CloseNegativeVisitsOneOffCommand.PrimaryEntityType,
                    primaryEntityId),
                new EntityId(
                    CloseNegativeVisitsOneOffCommand.CanonicalRereadEntityType,
                    closure.ClientId),
                auditEntryId: new AuditEntryId(auditEntryId));
        }

        return Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete negative closure request.",
            "idempotencyKey");
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        NormalizedOneOffNegativeClosure closure,
        DateTimeOffset recordedAt,
        Guid closureId,
        AuditEntryId auditEntryId,
        string fingerprint)
    {
        return new CommandIdempotencyRecord
        {
            Id = Guid.NewGuid(),
            CommandName = commandName,
            IdempotencyKey = closure.IdempotencyKey,
            RequestCorrelationId = closure.Envelope.RequestCorrelationId.Value!,
            AccountId = closure.Envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(closure.Envelope.Actor.Role),
            AccountKind = MapAccountKind(closure.Envelope.Actor.AccountKind),
            SessionId = closure.Envelope.Actor.SessionId.Value,
            DeviceLabel = closure.Envelope.Actor.DeviceLabel,
            EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                closure.Envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = closureId,
            RereadTargetId = closure.ClientId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
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
            && exception.ConstraintName is ActiveVisitConstraint
                or ClosureIdempotencyConstraint)
        {
            result = Error(
                exception.ConstraintName == ActiveVisitConstraint
                    ? CommandErrorCode.StaleState
                    : CommandErrorCode.DuplicateSubmission,
                exception.ConstraintName == ActiveVisitConstraint
                    ? "A selected negative Visit was covered concurrently. Refresh canonical state."
                    : "Negative closure with this idempotency key already exists.",
                exception.ConstraintName == ActiveVisitConstraint
                    ? "expectedOldestOpenNegativeVisitId"
                    : "idempotencyKey");
            return true;
        }

        result = null!;
        return false;
    }

    internal static CommandResult Success(
        Guid closureId,
        Guid clientId,
        Guid paymentId,
        IReadOnlyCollection<Guid> sourceMembershipIds,
        AuditEntryId auditEntryId,
        int remainingNegativeBalance)
    {
        var related = sourceMembershipIds
            .Select(id => new EntityId(MembershipAuditActions.MembershipEntityType, id))
            .Append(new EntityId("payment", paymentId))
            .ToArray();
        var warnings = remainingNegativeBalance > 0
            ? new[] { MembershipWarningCodes.NegativeBalance }
            : [];
        return CommandResult.Success(
            new EntityId(CloseNegativeVisitsOneOffCommand.PrimaryEntityType, closureId),
            new EntityId(
                CloseNegativeVisitsOneOffCommand.CanonicalRereadEntityType,
                clientId),
            related,
            warnings,
            auditEntryId);
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

    private static CommandResult? ValidateAndNormalizeEnvelope(
        CommandEnvelope envelope,
        out CommandEnvelope? normalized)
    {
        normalized = null;
        if (envelope?.Actor is null)
        {
            return Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to close negative Visits.");
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
                "This negative closure workflow currently accepts only normal entry origin.",
                "entryOrigin");
        }

        DateTimeOffset? occurredAt = null;
        if (envelope.OccurredAt is { } submittedOccurredAt)
        {
            if (!BusinessTimeZone.TryNormalizeUtcInstant(
                    submittedOccurredAt,
                    out var normalizedOccurredAt))
            {
                return ValidationError(
                    "Occurred_at is outside the supported business-calendar range.",
                    "occurredAt");
            }

            occurredAt = normalizedOccurredAt;
        }

        var reason = NormalizeOptional(envelope.Reason);
        var comment = NormalizeOptional(envelope.Comment);
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

        normalized = new CommandEnvelope(
            envelope.Actor with { DeviceLabel = deviceLabel },
            new RequestCorrelationId(correlationId),
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

    private static string MapAccountKind(AccountKind kind) => kind switch
    {
        AccountKind.Owner => "owner",
        AccountKind.NamedAdmin => "named_admin",
        AccountKind.SharedReceptionAdmin => "shared_reception_admin",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static string MapActorRole(ActorRole role) => role switch
    {
        ActorRole.Owner => "owner",
        ActorRole.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}

internal sealed record NormalizedOneOffNegativeClosure(
    Guid ClientId,
    Guid ExpectedOldestOpenNegativeVisitId,
    IReadOnlyList<NormalizedOneOffNegativeClosureLine> Lines,
    int VisitsCount,
    Guid? EntryBatchId,
    CommandEnvelope Envelope)
{
    public string IdempotencyKey => Envelope.IdempotencyKey!;
}

internal sealed record NormalizedOneOffNegativeClosureLine(
    Guid MembershipTypeId,
    DateTimeOffset ExpectedMembershipTypeUpdatedAt,
    int Quantity,
    int Sequence);
