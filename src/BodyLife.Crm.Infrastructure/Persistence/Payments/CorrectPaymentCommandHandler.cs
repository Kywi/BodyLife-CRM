using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public sealed class CorrectPaymentCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    IPaymentDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CorrectPaymentCommand>
{
    private const string CommandName = "CorrectPayment";

    public async Task<CommandResult> ExecuteAsync(
        CorrectPaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Envelope?.Actor is null
            || !PaymentCommandSupport.IsAllowedActorShape(command.Envelope.Actor))
        {
            return CorrectPaymentCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to correct a Payment.");
        }

        var validation = CorrectPaymentCommandSupport.ValidateAndNormalize(
            command,
            out var normalizedCorrection);
        if (validation is not null)
        {
            return validation;
        }

        var correction = normalizedCorrection!;
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = CorrectPaymentCommandSupport.CreateFingerprint(correction);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await PaymentCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    correction.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(CorrectPaymentCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await PaymentCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                correction.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken));
            }

            var original = await LockOriginalPaymentAsync(
                correction.OriginalPaymentId,
                cancellationToken);

            existingIdempotency = await PaymentCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                correction.Envelope.IdempotencyKey!,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await ReplayOrRejectDuplicateAsync(
                    existingIdempotency,
                    correction,
                    fingerprint,
                    cancellationToken));
            }

            if (original is null)
            {
                return await RollBackAsync(CorrectPaymentCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Original Payment was not found.",
                    "originalPaymentId"));
            }

            if (original.Status is "canceled" or "replaced")
            {
                return await RollBackAsync(
                    CorrectPaymentCommandSupport.AlreadyProcessed());
            }

            if (original.Status != "active"
                || original.Amount <= 0
                || !string.Equals(original.Method, "cash", StringComparison.Ordinal)
                || !CorrectPaymentCommandSupport.TryMapStoredPaymentContext(
                    original.PaymentContext,
                    out var originalPaymentContext)
                || await HasOutgoingCorrectionOrCancellationAsync(
                    original.Id,
                    cancellationToken))
            {
                return await RollBackAsync(
                    CorrectPaymentCommandSupport.ConcurrencyConflict());
            }

            if (originalPaymentContext == PaymentContext.NegativeClosure)
            {
                return await RollBackAsync(CorrectPaymentCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    "Negative-closure Payment correction requires its explicit Membership workflow.",
                    "originalPaymentId"));
            }

            var changedAfterClose = await IsChangedAfterCloseAsync(
                original.OccurredAt,
                correction.Replacement?.OccurredAt,
                cancellationToken);
            if (changedAfterClose
                && correction.Envelope.Actor.Role != ActorRole.Owner)
            {
                return await RollBackAsync(CorrectPaymentCommandSupport.Error(
                    CommandErrorCode.DayClosedRequiresOwner,
                    "Only the Owner can correct a Payment that affects a reconciled day.",
                    "originalPaymentId"));
            }

            IReadOnlyList<string> changedFields = [];
            if (correction.Replacement is { } candidateReplacement)
            {
                if (candidateReplacement.MembershipId is { } membershipId
                    && !await LockMembershipAsync(
                        original.ClientId,
                        membershipId,
                        cancellationToken))
                {
                    return await RollBackAsync(CorrectPaymentCommandSupport.Error(
                        CommandErrorCode.NotFound,
                        "Replacement Membership was not found for the Payment Client.",
                        "replacement.membershipId"));
                }

                changedFields = CorrectPaymentCommandSupport.IdentifyChangedFields(
                    original,
                    candidateReplacement);
                if (changedFields.Count == 0)
                {
                    return await RollBackAsync(
                        CorrectPaymentCommandSupport.ValidationError(
                            "Replacement must change at least one Payment field.",
                            "replacement"));
                }
            }

            var beforePayment = SummarizePayment(original);
            var primaryEntityId = Guid.NewGuid();
            Guid? replacementPaymentId = null;
            var actionType = correction.Mode == PaymentCorrectionMode.Replace
                ? PaymentAuditActions.Corrected
                : PaymentAuditActions.Canceled;
            object relatedEntityRefs;
            object afterSummary;

            if (correction.Mode == PaymentCorrectionMode.Replace)
            {
                var replacement = correction.Replacement!;
                replacementPaymentId = Guid.NewGuid();
                var replacementRecord = new PaymentRecord
                {
                    Id = replacementPaymentId.Value,
                    ClientId = original.ClientId,
                    MembershipId = replacement.MembershipId,
                    Amount = replacement.Amount.Amount,
                    Currency = replacement.Amount.Currency,
                    Method = "cash",
                    PaymentContext = PaymentCommandSupport.MapPaymentContext(
                        replacement.PaymentContext),
                    OccurredAt = replacement.OccurredAt,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                    EntryOrigin = PaymentCommandSupport.MapEntryOrigin(
                        correction.Envelope.EntryOrigin),
                    EntryBatchId = correction.EntryBatchId,
                    Comment = replacement.Comment,
                    Status = "active",
                };
                var correctionRecord = new PaymentCorrectionRecord
                {
                    Id = primaryEntityId,
                    ClientId = original.ClientId,
                    OriginalPaymentId = original.Id,
                    ReplacementPaymentId = replacementRecord.Id,
                    ChangedFieldsJson = CorrectPaymentCommandSupport
                        .SerializeChangedFields(changedFields),
                    Reason = correction.Envelope.Reason!,
                    OccurredAt = correction.Envelope.OccurredAt!.Value,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                    EntryOrigin = replacementRecord.EntryOrigin,
                    EntryBatchId = correction.EntryBatchId,
                };

                original.Status = "replaced";
                dbContext.Set<PaymentRecord>().Add(replacementRecord);
                dbContext.Set<PaymentCorrectionRecord>().Add(correctionRecord);
                relatedEntityRefs = new
                {
                    original.ClientId,
                    OriginalPaymentId = original.Id,
                    OriginalMembershipId = original.MembershipId,
                    ReplacementPaymentId = replacementRecord.Id,
                    ReplacementMembershipId = replacementRecord.MembershipId,
                    CorrectionId = correctionRecord.Id,
                };
                afterSummary = new
                {
                    Correction = new
                    {
                        CorrectionId = correctionRecord.Id,
                        correctionRecord.OriginalPaymentId,
                        correctionRecord.ReplacementPaymentId,
                        ChangedFields = changedFields,
                        correctionRecord.Reason,
                        correctionRecord.OccurredAt,
                        correctionRecord.RecordedAt,
                        correctionRecord.EntryOrigin,
                        correctionRecord.EntryBatchId,
                        ChangedAfterClose = changedAfterClose,
                    },
                    OriginalPayment = beforePayment with { Status = "replaced" },
                    ReplacementPayment = SummarizePayment(replacementRecord),
                };
            }
            else
            {
                var cancellationRecord = new PaymentCancellationRecord
                {
                    Id = primaryEntityId,
                    PaymentId = original.Id,
                    Reason = correction.Envelope.Reason!,
                    OccurredAt = correction.Envelope.OccurredAt!.Value,
                    RecordedAt = recordedAt,
                    RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    SessionId = correction.Envelope.Actor.SessionId.Value,
                    EntryOrigin = PaymentCommandSupport.MapEntryOrigin(
                        correction.Envelope.EntryOrigin),
                    EntryBatchId = correction.EntryBatchId,
                };

                original.Status = "canceled";
                dbContext.Set<PaymentCancellationRecord>().Add(cancellationRecord);
                relatedEntityRefs = new
                {
                    original.ClientId,
                    PaymentId = original.Id,
                    original.MembershipId,
                    CancellationId = cancellationRecord.Id,
                };
                afterSummary = new
                {
                    Cancellation = new
                    {
                        CancellationId = cancellationRecord.Id,
                        cancellationRecord.PaymentId,
                        cancellationRecord.Reason,
                        cancellationRecord.OccurredAt,
                        cancellationRecord.RecordedAt,
                        cancellationRecord.EntryOrigin,
                        cancellationRecord.EntryBatchId,
                        ChangedAfterClose = changedAfterClose,
                    },
                    Payment = beforePayment with { Status = "canceled" },
                };
            }

            var auditEntryId = auditAppender.Append(
                correction.Envelope,
                actionType,
                PaymentAuditActions.EntityType,
                original.Id,
                recordedAt,
                relatedEntityRefs,
                beforeSummary: new { Payment = beforePayment },
                afterSummary,
                changedAfterClose);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                CorrectPaymentCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    correction,
                    recordedAt,
                    primaryEntityId,
                    original.ClientId,
                    auditEntryId,
                    fingerprint));

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CorrectPaymentCommandSupport.Success(
                correction.Mode,
                primaryEntityId,
                original.Id,
                replacementPaymentId,
                original.ClientId,
                auditEntryId,
                changedAfterClose);
        }
        catch (Exception exception)
        {
            var postgresException = PaymentCommandSupport.FindPostgresException(exception);
            if (postgresException is not null
                && CorrectPaymentCommandSupport.TryMapPostgresFailure(
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

    private async Task<PaymentRecord?> LockOriginalPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var payments = await dbContext.Set<PaymentRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.payments
                where id = {paymentId}
                for update
                """)
            .ToArrayAsync(cancellationToken);
        return payments.SingleOrDefault();
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

    private async Task<bool> HasOutgoingCorrectionOrCancellationAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<PaymentCancellationRecord>()
                .AsNoTracking()
                .AnyAsync(
                    cancellation => cancellation.PaymentId == paymentId,
                    cancellationToken)
            || await dbContext.Set<PaymentCorrectionRecord>()
                .AsNoTracking()
                .AnyAsync(
                    correction => correction.OriginalPaymentId == paymentId,
                    cancellationToken);
    }

    private async Task<bool> IsChangedAfterCloseAsync(
        DateTimeOffset originalOccurredAt,
        DateTimeOffset? replacementOccurredAt,
        CancellationToken cancellationToken)
    {
        var businessDates = new HashSet<DateOnly>
        {
            BusinessTimeZone.GetBusinessDate(originalOccurredAt),
        };
        if (replacementOccurredAt is { } replacementTime)
        {
            businessDates.Add(BusinessTimeZone.GetBusinessDate(replacementTime));
        }

        var changedAfterClose = false;
        foreach (var businessDate in businessDates)
        {
            var status = await dayReconciliationStatusProvider.GetStatusAsync(
                businessDate,
                cancellationToken);
            if (!Enum.IsDefined(status))
            {
                throw new InvalidOperationException(
                    $"Payment day reconciliation status '{status}' is not supported.");
            }

            changedAfterClose |= status == PaymentDayReconciliationStatus.Reconciled;
        }

        return changedAfterClose;
    }

    private async Task<CommandResult> ReplayOrRejectDuplicateAsync(
        CommandIdempotencyRecord record,
        NormalizedCorrectPayment correction,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        if (!CorrectPaymentCommandSupport.TryGetSuccessfulReplay(
                record,
                correction,
                fingerprint,
                out var primaryEntityId,
                out var clientId,
                out var auditEntryId))
        {
            return CorrectPaymentCommandSupport.DuplicateSubmission();
        }

        var expectedAction = correction.Mode == PaymentCorrectionMode.Replace
            ? PaymentAuditActions.Corrected
            : PaymentAuditActions.Canceled;
        var audit = await dbContext.Set<BusinessAuditEntryRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entry => entry.Id == auditEntryId.Value
                    && entry.ActionType == expectedAction
                    && entry.EntityType == PaymentAuditActions.EntityType
                    && entry.EntityId == correction.OriginalPaymentId,
                cancellationToken);
        if (audit is null)
        {
            return CorrectPaymentCommandSupport.DuplicateSubmission();
        }

        Guid? replacementPaymentId = null;
        if (correction.Mode == PaymentCorrectionMode.Replace)
        {
            var correctionRecord = await dbContext.Set<PaymentCorrectionRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    candidate => candidate.Id == primaryEntityId
                        && candidate.ClientId == clientId
                        && candidate.OriginalPaymentId == correction.OriginalPaymentId,
                    cancellationToken);
            if (correctionRecord is null
                || !await dbContext.Set<PaymentRecord>()
                    .AsNoTracking()
                    .AnyAsync(
                        payment => payment.Id == correctionRecord.ReplacementPaymentId
                            && payment.ClientId == clientId,
                        cancellationToken))
            {
                return CorrectPaymentCommandSupport.DuplicateSubmission();
            }

            replacementPaymentId = correctionRecord.ReplacementPaymentId;
        }
        else if (!await (
                from cancellation in dbContext.Set<PaymentCancellationRecord>()
                    .AsNoTracking()
                join payment in dbContext.Set<PaymentRecord>().AsNoTracking()
                    on cancellation.PaymentId equals payment.Id
                where cancellation.Id == primaryEntityId
                    && cancellation.PaymentId == correction.OriginalPaymentId
                    && payment.ClientId == clientId
                select cancellation.Id)
            .AnyAsync(cancellationToken))
        {
            return CorrectPaymentCommandSupport.DuplicateSubmission();
        }

        return CorrectPaymentCommandSupport.Success(
            correction.Mode,
            primaryEntityId,
            correction.OriginalPaymentId,
            replacementPaymentId,
            clientId,
            auditEntryId,
            audit.ChangedAfterClose);
    }

    private static PaymentAuditSummary SummarizePayment(PaymentRecord payment)
    {
        return new PaymentAuditSummary(
            payment.Id,
            payment.ClientId,
            payment.MembershipId,
            payment.Amount,
            payment.Currency,
            payment.Method,
            payment.PaymentContext,
            payment.OccurredAt,
            payment.RecordedAt,
            payment.RecordedByAccountId,
            payment.SessionId,
            payment.EntryOrigin,
            payment.EntryBatchId,
            payment.Comment,
            payment.Status);
    }

    private sealed record PaymentAuditSummary(
        Guid PaymentId,
        Guid ClientId,
        Guid? MembershipId,
        decimal Amount,
        string Currency,
        string Method,
        string PaymentContext,
        DateTimeOffset OccurredAt,
        DateTimeOffset RecordedAt,
        Guid RecordedByAccountId,
        Guid SessionId,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment,
        string Status);
}
