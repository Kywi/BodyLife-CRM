using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.MembershipTypes;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

public sealed class EditMembershipTypeCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<EditMembershipTypeCommand>
{
    private const string CommandName = "EditMembershipType";

    public async Task<CommandResult> ExecuteAsync(
        EditMembershipTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !MembershipTypeCommandSupport.IsOwnerActorShape(command.Envelope.Actor))
        {
            return MembershipTypeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner session is required to edit a membership type.");
        }

        var validationResult = MembershipTypeCommandSupport.ValidateAndNormalizeEdit(
            command,
            out var normalizedEdit);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var edit = normalizedEdit!;
        var canonicalEnvelope = edit.Envelope.CanonicalEnvelope;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = MembershipTypeCommandSupport.CreateEditFingerprint(
            canonicalEnvelope,
            edit);
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
                edit.MembershipTypeId,
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
                edit.Envelope.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return MembershipTypeCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    canonicalEnvelope.Actor.AccountId.Value,
                    fingerprint);
            }

            if (membershipType.UpdatedAt != edit.ExpectedUpdatedAt)
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "Membership type changed after the edit form was loaded. Refresh canonical state.",
                    "expectedUpdatedAt");
            }

            var before = MembershipTypeCatalogSnapshot.From(membershipType);

            if (before.Matches(edit.CatalogValues))
            {
                return MembershipTypeCommandSupport.ValidationError(
                    "At least one membership type catalog field must change.",
                    field: null);
            }

            var catalog = edit.CatalogValues;
            membershipType.Name = catalog.Name;
            membershipType.DurationDays = catalog.DurationDays;
            membershipType.VisitsLimit = catalog.VisitsLimit;
            membershipType.PriceAmount = catalog.Price.Amount;
            membershipType.PriceCurrency = catalog.Price.Currency;
            membershipType.Comment = catalog.Comment;
            membershipType.UpdatedAt = NextUpdatedAt(membershipType.UpdatedAt, recordedAt);

            var auditEntryId = auditAppender.Append(
                canonicalEnvelope,
                MembershipTypeAuditActions.Edited,
                MembershipTypeAuditActions.EntityType,
                membershipType.Id,
                recordedAt,
                beforeSummary: before,
                afterSummary: MembershipTypeCatalogSnapshot.From(membershipType));

            dbContext.Set<CommandIdempotencyRecord>().Add(
                MembershipTypeCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    canonicalEnvelope,
                    edit.Envelope,
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

    private sealed record MembershipTypeCatalogSnapshot(
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
        internal static MembershipTypeCatalogSnapshot From(MembershipTypeRecord membershipType)
        {
            return new MembershipTypeCatalogSnapshot(
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

        internal bool Matches(MembershipTypeCatalogValues catalog)
        {
            return Name == catalog.Name
                && DurationDays == catalog.DurationDays
                && VisitsLimit == catalog.VisitsLimit
                && Price.Amount == catalog.Price.Amount
                && Price.Currency == catalog.Price.Currency
                && Comment == catalog.Comment;
        }
    }

    private sealed record MembershipTypePriceSnapshot(decimal Amount, string Currency);
}
