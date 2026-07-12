using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Modules.MembershipTypes;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;

public sealed class CreateMembershipTypeCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreateMembershipTypeCommand>
{
    private const string CommandName = "CreateMembershipType";

    public async Task<CommandResult> ExecuteAsync(
        CreateMembershipTypeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !MembershipTypeCommandSupport.IsOwnerActorShape(command.Envelope.Actor))
        {
            return MembershipTypeCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner session is required to create a membership type.");
        }

        var validationResult = MembershipTypeCommandSupport.ValidateAndNormalizeCreate(
            command,
            out var normalizedCreate);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var create = normalizedCreate!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = MembershipTypeCommandSupport.CreateFingerprint(command.Envelope, create);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipTypeCommandSupport.IsCanonicalOwnerAuthorizedAsync(
                    dbContext,
                    command.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return MembershipTypeCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner account or session is not active.");
            }

            var existingIdempotency = await MembershipTypeCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                create.Envelope.IdempotencyKey,
                cancellationToken);

            if (existingIdempotency is not null)
            {
                return MembershipTypeCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    command.Envelope.Actor.AccountId.Value,
                    fingerprint);
            }

            var membershipTypeId = Guid.NewGuid();
            var catalog = create.CatalogValues;
            var membershipType = new MembershipTypeRecord
            {
                Id = membershipTypeId,
                Name = catalog.Name,
                DurationDays = catalog.DurationDays,
                VisitsLimit = catalog.VisitsLimit,
                PriceAmount = catalog.Price.Amount,
                PriceCurrency = catalog.Price.Currency,
                IsActive = create.IsActive,
                Comment = catalog.Comment,
                CreatedAt = recordedAt,
                UpdatedAt = recordedAt,
                DeactivatedAt = create.IsActive ? null : recordedAt,
            };
            dbContext.Set<MembershipTypeRecord>().Add(membershipType);

            var auditEntryId = auditAppender.Append(
                command.Envelope,
                MembershipTypeAuditActions.Created,
                MembershipTypeAuditActions.EntityType,
                membershipTypeId,
                recordedAt,
                afterSummary: new
                {
                    membershipType.Name,
                    membershipType.DurationDays,
                    membershipType.VisitsLimit,
                    Price = new
                    {
                        Amount = membershipType.PriceAmount,
                        Currency = membershipType.PriceCurrency,
                    },
                    membershipType.IsActive,
                    membershipType.Comment,
                    membershipType.CreatedAt,
                    membershipType.UpdatedAt,
                    membershipType.DeactivatedAt,
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                MembershipTypeCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    command.Envelope,
                    create,
                    recordedAt,
                    membershipTypeId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return MembershipTypeCommandSupport.Success(membershipTypeId, auditEntryId);
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
}
