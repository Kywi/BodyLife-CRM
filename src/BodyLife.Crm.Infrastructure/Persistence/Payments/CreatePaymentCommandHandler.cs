using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Payments;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class CreatePaymentCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CreatePaymentCommand>
{
    private const string CommandName = "CreatePayment";

    public async Task<CommandResult> ExecuteAsync(
        CreatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !PaymentCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return PaymentCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to create a Payment.");
        }

        var validation = PaymentCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedPayment);
        if (validation is not null)
        {
            return validation;
        }

        var payment = normalizedPayment!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = PaymentCommandSupport.CreateFingerprint(payment);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await PaymentCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    payment.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(PaymentCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await PaymentCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                payment.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(ReplayOrRejectDuplicate(
                    existingIdempotency,
                    payment,
                    fingerprint));
            }

            if (!await LockClientAsync(payment.ClientId, cancellationToken))
            {
                return await RollBackAsync(PaymentCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId"));
            }

            existingIdempotency = await PaymentCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                payment.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(ReplayOrRejectDuplicate(
                    existingIdempotency,
                    payment,
                    fingerprint));
            }

            if (payment.MembershipId is { } membershipId
                && !await LockMembershipAsync(
                    payment.ClientId,
                    membershipId,
                    cancellationToken))
            {
                return await RollBackAsync(PaymentCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Selected Membership was not found for the Client.",
                    "membershipId"));
            }

            var paymentId = Guid.NewGuid();
            var paymentRecord = new PaymentRecord
            {
                Id = paymentId,
                ClientId = payment.ClientId,
                MembershipId = payment.MembershipId,
                Amount = payment.Amount.Amount,
                Currency = payment.Amount.Currency,
                Method = "cash",
                PaymentContext = PaymentCommandSupport.MapPaymentContext(
                    payment.PaymentContext),
                OccurredAt = payment.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = payment.Envelope.Actor.AccountId.Value,
                SessionId = payment.Envelope.Actor.SessionId.Value,
                EntryOrigin = PaymentCommandSupport.MapEntryOrigin(
                    payment.Envelope.EntryOrigin),
                EntryBatchId = payment.EntryBatchId,
                Comment = payment.Envelope.Comment,
                Status = "active",
            };
            dbContext.Set<PaymentRecord>().Add(paymentRecord);

            var auditEntryId = auditAppender.Append(
                payment.Envelope,
                PaymentAuditActions.Created,
                PaymentAuditActions.EntityType,
                paymentId,
                recordedAt,
                relatedEntityRefs: new
                {
                    payment.ClientId,
                    payment.MembershipId,
                },
                afterSummary: new
                {
                    Payment = new
                    {
                        PaymentId = paymentId,
                        paymentRecord.ClientId,
                        paymentRecord.MembershipId,
                        paymentRecord.Amount,
                        paymentRecord.Currency,
                        paymentRecord.Method,
                        paymentRecord.PaymentContext,
                        paymentRecord.OccurredAt,
                        paymentRecord.RecordedAt,
                        paymentRecord.EntryOrigin,
                        paymentRecord.EntryBatchId,
                        paymentRecord.Comment,
                        paymentRecord.Status,
                    },
                });

            dbContext.Set<CommandIdempotencyRecord>().Add(
                PaymentCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    payment,
                    recordedAt,
                    paymentId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return PaymentCommandSupport.Success(
                paymentId,
                payment,
                auditEntryId);
        }
        catch (Exception exception)
        {
            var postgresException = PaymentCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && PaymentCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var errorResult))
            {
                return await RollBackAsync(errorResult);
            }

            await PaymentCommandSupport.RollBackAndClearAsync(dbContext, transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            return await PaymentCommandSupport.RollBackAndReturnAsync(
                dbContext,
                transaction,
                result);
        }
    }

    private async Task<bool> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var clients = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.clients
                where id = {clientId}
                for update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return clients.Length == 1;
    }

    private async Task<bool> LockMembershipAsync(
        Guid clientId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var memberships = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.issued_memberships
                where id = {membershipId}
                    and client_id = {clientId}
                for key share
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return memberships.Length == 1;
    }

    private static CommandResult ReplayOrRejectDuplicate(
        CommandIdempotencyRecord record,
        NormalizedCreatePayment payment,
        string fingerprint)
    {
        return PaymentCommandSupport.TryGetSuccessfulReplay(
            record,
            payment,
            fingerprint,
            out var paymentId,
            out var auditEntryId)
                ? PaymentCommandSupport.Success(paymentId, payment, auditEntryId)
                : PaymentCommandSupport.DuplicateSubmission();
    }
}
