using System.Security.Cryptography;
using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;

internal static class CorrectNonWorkingDayCommandSupport
{
    private const string SucceededIdempotencyStatus = "succeeded";
    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);

    internal static string CreateFingerprint(
        CorrectNonWorkingDayPreparation correction)
    {
        var envelope = correction.Envelope;
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            ActorAccountId = envelope.Actor.AccountId.Value,
            ActorRole = MapActorRole(envelope.Actor.Role),
            ActorAccountKind = MapAccountKind(envelope.Actor.AccountKind),
            ActorSessionId = envelope.Actor.SessionId.Value,
            EntryOrigin = NonWorkingDayCommandSupport.MapEntryOrigin(
                envelope.EntryOrigin),
            envelope.OccurredAt,
            EnvelopeReason = envelope.Reason,
            EnvelopeComment = envelope.Comment,
            correction.PeriodId,
            Mode = MapMode(correction.Mode),
            ReplacementStartDate = correction.ReplacementPeriod?.StartDate,
            ReplacementEndDate = correction.ReplacementPeriod?.EndDate,
            correction.ReplacementReasonCode,
            correction.ReplacementReasonComment,
            correction.ConfirmationToken,
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }

    internal static bool TryGetSuccessfulReplay(
        CommandIdempotencyRecord record,
        CorrectNonWorkingDayPreparation correction,
        string fingerprint,
        out Guid primaryEntityId,
        out AuditEntryId auditEntryId)
    {
        if (record.Status == SucceededIdempotencyStatus
            && record.AccountId == correction.Envelope.Actor.AccountId.Value
            && string.Equals(record.ResultFingerprint, fingerprint, StringComparison.Ordinal)
            && record.PrimaryEntityId is { } primaryId
            && primaryId != Guid.Empty
            && record.RereadTargetId == correction.PeriodId
            && record.AuditEntryId is { } auditId
            && auditId != Guid.Empty)
        {
            primaryEntityId = primaryId;
            auditEntryId = new AuditEntryId(auditId);
            return true;
        }

        primaryEntityId = Guid.Empty;
        auditEntryId = default;
        return false;
    }

    internal static CommandIdempotencyRecord CreateSucceededIdempotencyRecord(
        string commandName,
        CorrectNonWorkingDayPreparation correction,
        DateTimeOffset recordedAt,
        Guid primaryEntityId,
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
            EntryOrigin = NonWorkingDayCommandSupport.MapEntryOrigin(
                envelope.EntryOrigin),
            Status = SucceededIdempotencyStatus,
            CreatedAt = recordedAt,
            CompletedAt = recordedAt,
            ExpiresAt = recordedAt.Add(IdempotencyRetention),
            PrimaryEntityId = primaryEntityId,
            RereadTargetId = correction.PeriodId,
            AuditEntryId = auditEntryId.Value,
            ResultFingerprint = fingerprint,
        };
    }

    internal static async Task<IReadOnlyList<Guid>> ReadAffectedMembershipIdsAsync(
        BodyLifeDbContext dbContext,
        Guid originalPeriodId,
        Guid? replacementPeriodId,
        CancellationToken cancellationToken)
    {
        var periodIds = replacementPeriodId is { } replacementId
            ? new[] { originalPeriodId, replacementId }
            : [originalPeriodId];

        return await dbContext.Set<NonWorkingPeriodApplicationRecord>()
            .AsNoTracking()
            .Where(application => periodIds.Contains(application.NonWorkingPeriodId))
            .Select(application => application.MembershipId)
            .Distinct()
            .Order()
            .ToArrayAsync(cancellationToken);
    }

    internal static CommandResult Success(
        NonWorkingDayCorrectionMode mode,
        Guid primaryEntityId,
        Guid originalPeriodId,
        IReadOnlyList<Guid> membershipIds,
        AuditEntryId auditEntryId)
    {
        var primaryEntityType = mode == NonWorkingDayCorrectionMode.Cancel
            ? CorrectNonWorkingDayCommand.CancellationEntityType
            : CorrectNonWorkingDayCommand.PeriodEntityType;
        var relatedEntityIds = new List<EntityId>(membershipIds.Count + 1)
        {
            new(CorrectNonWorkingDayCommand.PeriodEntityType, originalPeriodId),
        };
        relatedEntityIds.AddRange(
            membershipIds
                .Order()
                .Select(membershipId => new EntityId(
                    CorrectNonWorkingDayCommand.MembershipEntityType,
                    membershipId)));

        return CommandResult.Success(
            new EntityId(primaryEntityType, primaryEntityId),
            new EntityId(
                CorrectNonWorkingDayCommand.CanonicalRereadEntityType,
                originalPeriodId),
            relatedEntityIds,
            auditEntryId: auditEntryId);
    }

    internal static CommandResult DuplicateSubmission()
    {
        return NonWorkingDayCommandSupport.Error(
            CommandErrorCode.DuplicateSubmission,
            "Idempotency key has already been used by a different or incomplete CorrectNonWorkingDay request.",
            "idempotencyKey");
    }

    internal static CommandResult SourceChangedConcurrently()
    {
        return NonWorkingDayCommandSupport.Error(
            CommandErrorCode.ConcurrencyConflict,
            "NonWorkingDay correction source changed. Refresh canonical state and create a new preview.");
    }

    internal static CommandResult RecalculationFailed()
    {
        return NonWorkingDayCommandSupport.Error(
            CommandErrorCode.RecalculationFailed,
            "Affected Membership state could not be rebuilt after NonWorkingDay correction.");
    }

    private static string MapMode(NonWorkingDayCorrectionMode mode)
    {
        return mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange => "replace_range",
            NonWorkingDayCorrectionMode.ReplaceReason => "replace_reason",
            NonWorkingDayCorrectionMode.Cancel => "cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
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
