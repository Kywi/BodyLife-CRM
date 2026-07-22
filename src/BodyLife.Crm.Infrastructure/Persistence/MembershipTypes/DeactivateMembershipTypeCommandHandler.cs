using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.MembershipTypes;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

public sealed class DeactivateMembershipTypeCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<DeactivateMembershipTypeCommand>
{
    private const string CommandName = "DeactivateMembershipType";

    public async Task<CommandResult> ExecuteAsync(
        DeactivateMembershipTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !MembershipTypeCommandSupport.IsOwnerActorShape(command.Envelope.Actor))
        {
            return MembershipTypeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner session is required to deactivate a membership type.");
        }

        var validationResult = MembershipTypeCommandSupport.ValidateAndNormalizeDeactivate(
            command,
            out var normalizedDeactivation);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var deactivation = normalizedDeactivation!;
        var canonicalEnvelope = deactivation.Envelope.CanonicalEnvelope;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = MembershipTypeCommandSupport.CreateDeactivateFingerprint(
            canonicalEnvelope,
            deactivation);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipTypeCommandSupport.IsCanonicalOwnerAuthorizedAsync(
                    dbContext,
                    canonicalEnvelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner account or session is not active.");
            }

            var membershipType = await LockMembershipTypeAsync(
                deactivation.MembershipTypeId,
                cancellationToken);

            if (membershipType is null)
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Membership type was not found.",
                    "membershipTypeId");
            }

            var existingIdempotency = await MembershipTypeCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                deactivation.Envelope.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return MembershipTypeCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    canonicalEnvelope.Actor.AccountId.Value,
                    fingerprint);
            }

            if (membershipType.UpdatedAt != deactivation.ExpectedUpdatedAt)
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Membership type changed after the catalog state was loaded. Refresh canonical state.",
                    "expectedUpdatedAt");
            }

            if (!membershipType.IsActive)
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.AlreadyInactive,
                    "Membership type is already inactive.",
                    "membershipTypeId");
            }

            var before = MembershipTypeLifecycleSnapshot.From(membershipType);
            var lifecycleTimestamp = NextUpdatedAt(membershipType.UpdatedAt, recordedAt);
            membershipType.IsActive = false;
            membershipType.UpdatedAt = lifecycleTimestamp;
            membershipType.DeactivatedAt = lifecycleTimestamp;

            var auditEntryId = auditAppender.Append(
                canonicalEnvelope,
                MembershipTypeAuditActions.Deactivated,
                MembershipTypeAuditActions.EntityType,
                membershipType.Id,
                recordedAt,
                beforeSummary: before,
                afterSummary: MembershipTypeLifecycleSnapshot.From(membershipType));

            dbContext.Set<CommandIdempotencyRecord>().Add(
                MembershipTypeCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    canonicalEnvelope,
                    deactivation.Envelope,
                    recordedAt,
                    membershipType.Id,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MembershipTypeCommandSupport.Success(membershipType.Id, auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipTypeCommandSupport.FindPostgresException(exception);

            if (postgresException is null
                || !MembershipTypeCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                throw;
            }

            await MembershipTypeCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            return errorResult;
        }
    }

    private async Task<MembershipTypeRecord?> LockMembershipTypeAsync(
        Guid membershipTypeId,
        CancellationToken cancellationToken)
    {
        var membershipTypes = await dbContext.Set<MembershipTypeRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.membership_types
                where id = {membershipTypeId}
                for update
                """)
            .ToArrayAsync(cancellationToken);
        return membershipTypes.SingleOrDefault();
    }

    private static DateTimeOffset NextUpdatedAt(
        DateTimeOffset previousUpdatedAt,
        DateTimeOffset recordedAt)
    {
        return recordedAt > previousUpdatedAt
            ? recordedAt
            : previousUpdatedAt.AddTicks(10);
    }

    private sealed record MembershipTypeLifecycleSnapshot(
        string Name,
        int DurationDays,
        int VisitsLimit,
        MembershipTypePriceSnapshot Price,
        bool IsActive,
        string? Comment,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        DateTimeOffset? DeactivatedAt)
    {
        internal static MembershipTypeLifecycleSnapshot From(MembershipTypeRecord membershipType)
        {
            return new MembershipTypeLifecycleSnapshot(
                membershipType.Name,
                membershipType.DurationDays,
                membershipType.VisitsLimit,
                new MembershipTypePriceSnapshot(
                    membershipType.PriceAmount,
                    membershipType.PriceCurrency),
                membershipType.IsActive,
                membershipType.Comment,
                membershipType.CreatedAt,
                membershipType.UpdatedAt,
                membershipType.DeactivatedAt);
        }
    }

    private sealed record MembershipTypePriceSnapshot(decimal Amount, string Currency);
}
