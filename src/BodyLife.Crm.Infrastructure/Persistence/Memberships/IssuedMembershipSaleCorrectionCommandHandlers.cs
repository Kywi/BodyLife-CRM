using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Freezes;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Infrastructure.Persistence.Visits;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class ReplaceIssuedMembershipCommandHandler(
    IssuedMembershipSaleCorrectionCommandExecutor executor)
    : IBodyLifeCommandHandler<ReplaceIssuedMembershipCommand>
{
    public Task<CommandResult> ExecuteAsync(
        ReplaceIssuedMembershipCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = IssuedMembershipSaleCorrectionCommandSupport
            .ValidateAndNormalize(command, out var correction);
        return validation is null
            ? executor.ExecuteAsync(
                "ReplaceIssuedMembership",
                correction!,
                cancellationToken)
            : Task.FromResult(validation);
    }
}

public sealed class CancelIssuedMembershipSaleCommandHandler(
    IssuedMembershipSaleCorrectionCommandExecutor executor)
    : IBodyLifeCommandHandler<CancelIssuedMembershipSaleCommand>
{
    public Task<CommandResult> ExecuteAsync(
        CancelIssuedMembershipSaleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = IssuedMembershipSaleCorrectionCommandSupport
            .ValidateAndNormalize(command, out var correction);
        return validation is null
            ? executor.ExecuteAsync(
                "CancelIssuedMembershipSale",
                correction!,
                cancellationToken)
            : Task.FromResult(validation);
    }
}

public sealed class IssuedMembershipSaleCorrectionCommandExecutor(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    IMembershipIssuePaymentWriter paymentWriter,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    IPaymentDayReconciliationStatusProvider dayReconciliationStatusProvider,
    TimeProvider timeProvider)
{
    internal async Task<CommandResult> ExecuteAsync(
        string commandName,
        NormalizedIssuedMembershipSaleCorrection correction,
        CancellationToken cancellationToken)
    {
        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = IssuedMembershipSaleCorrectionCommandSupport
            .CreateFingerprint(correction);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    correction.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active."));
            }

            var existingIdempotency = await MembershipCommandSupport
                .FindIdempotencyAsync(
                    dbContext,
                    commandName,
                    correction.IdempotencyKey,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await IssuedMembershipSaleCorrectionCommandSupport
                    .ReplayOrRejectDuplicateAsync(
                        dbContext,
                        existingIdempotency,
                        correction,
                        fingerprint,
                        cancellationToken));
            }

            var initialMembership = await dbContext.Set<IssuedMembershipRecord>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    membership => membership.Id == correction.OriginalMembershipId,
                    cancellationToken);
            if (initialMembership is null)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.NotFound,
                    "Issued Membership sale was not found.",
                    "originalMembershipId"));
            }

            var client = await LockClientAsync(
                initialMembership.ClientId,
                cancellationToken);
            if (client is null)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "originalMembershipId"));
            }

            existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                commandName,
                correction.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return await RollBackAsync(await IssuedMembershipSaleCorrectionCommandSupport
                    .ReplayOrRejectDuplicateAsync(
                        dbContext,
                        existingIdempotency,
                        correction,
                        fingerprint,
                        cancellationToken));
            }

            var originalMembership = await LockMembershipAsync(
                correction.OriginalMembershipId,
                cancellationToken);
            if (originalMembership is null
                || originalMembership.ClientId != client.Id)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.StaleState,
                    "Issued Membership sale changed after preview. Refresh canonical state.",
                    "originalMembershipId"));
            }

            MembershipTypeRecord? replacementType = null;
            if (correction.Mode == IssuedMembershipSaleCorrectionMode.Replace)
            {
                replacementType = await LockMembershipTypeAsync(
                    correction.ReplacementMembershipTypeId!.Value,
                    cancellationToken);
                if (replacementType is null)
                {
                    return await RollBackAsync(Error(
                        CommandErrorCode.NotFound,
                        "Replacement Membership type was not found.",
                        "replacementMembershipTypeId"));
                }

                if (!replacementType.IsActive)
                {
                    return await RollBackAsync(Error(
                        CommandErrorCode.MembershipTypeInactive,
                        "Inactive Membership type cannot replace a sale.",
                        "replacementMembershipTypeId"));
                }

                if (!IsEligibleOrdinaryType(replacementType))
                {
                    return await RollBackAsync(Error(
                        CommandErrorCode.MembershipNotEligible,
                        "Only an ordinary Membership type can replace a sale.",
                        "replacementMembershipTypeId"));
                }

                if (replacementType.UpdatedAt
                    != correction.ExpectedMembershipTypeUpdatedAt)
                {
                    return await RollBackAsync(Error(
                        CommandErrorCode.StaleState,
                        "Replacement Membership type changed after preview.",
                        "expectedMembershipTypeUpdatedAt"));
                }
            }

            await LockDependencyRowsAsync(
                originalMembership.Id,
                cancellationToken);
            var originalPayments = await LockSalePaymentsAsync(
                originalMembership.Id,
                cancellationToken);
            var alreadyCorrected = await dbContext
                .Set<IssuedMembershipSaleCorrectionRecord>()
                .AsNoTracking()
                .AnyAsync(
                    row => row.OriginalMembershipId == originalMembership.Id,
                    cancellationToken);
            if (alreadyCorrected
                || originalPayments.Length != 1
                || !IssuedMembershipSaleCorrectionSupport.IsExactActiveSale(
                    originalMembership,
                    originalPayments[0]))
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.StaleState,
                    "Only an active, uncorrected exact Membership sale can be changed.",
                    "originalMembershipId"));
            }

            var originalPayment = originalPayments[0];
            var changedAfterClose = await IsChangedAfterCloseAsync(
                originalPayment.OccurredAt,
                correction.Mode == IssuedMembershipSaleCorrectionMode.Replace
                    ? correction.Envelope.OccurredAt
                    : null,
                cancellationToken);
            var dependencies = await IssuedMembershipSaleCorrectionSupport
                .ReadDependenciesAsync(
                    dbContext,
                    originalMembership.Id,
                    originalMembership.ClientId,
                    cancellationToken);
            if (!dependencies.IsConsistent)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.RecalculationFailed,
                    "The Membership dependency set is inconsistent."));
            }

            var currentToken = IssuedMembershipSaleCorrectionSupport
                .CreateDependencyToken(
                    originalMembership,
                    originalPayment,
                    dependencies.Dependencies);
            if (!string.Equals(
                    currentToken,
                    correction.ExpectedDependencyToken,
                    StringComparison.Ordinal))
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.StaleState,
                    "Membership dependencies changed after preview. Refresh canonical state.",
                    "expectedDependencyToken"));
            }

            if (dependencies.Dependencies.Count > 0)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.MembershipNotEligible,
                    "The sale has active Visits, Freezes, NonWorkingDay applications or negative coverage. Resolve those dependencies first.",
                    "originalMembershipId"));
            }

            var correctionId = Guid.NewGuid();
            var beforeMembership = SummarizeMembership(originalMembership);
            var beforePayment = SummarizePayment(originalPayment);
            Guid? replacementMembershipId = null;
            Guid? replacementPaymentId = null;
            AuditEntryId? replacementPaymentAuditId = null;
            IssuedMembershipRecord? replacementMembership = null;
            PaymentRecord? replacementPayment = null;

            if (correction.Mode == IssuedMembershipSaleCorrectionMode.Replace)
            {
                var baseEndDate = MembershipDateRules.CalculateBaseEndDate(
                    correction.ReplacementStartDate!.Value,
                    replacementType!.DurationDays);
                replacementMembershipId = Guid.NewGuid();
                replacementMembership = new IssuedMembershipRecord
                {
                    Id = replacementMembershipId.Value,
                    ClientId = originalMembership.ClientId,
                    MembershipTypeId = replacementType.Id,
                    TypeNameSnapshot = replacementType.Name,
                    DurationDaysSnapshot = replacementType.DurationDays,
                    VisitsLimitSnapshot = replacementType.VisitsLimit,
                    PriceAmountSnapshot = replacementType.PriceAmount,
                    PriceCurrencySnapshot = replacementType.PriceCurrency,
                    IssuanceMode = IssuedMembershipSaleCorrectionSupport.SaleMode,
                    StartDate = correction.ReplacementStartDate.Value,
                    BaseEndDate = baseEndDate,
                    IssuedAt = recordedAt,
                    IssuedByAccountId = correction.Envelope.Actor.AccountId.Value,
                    Status = IssuedMembershipSaleCorrectionSupport.ActiveStatus,
                    EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                        correction.Envelope.EntryOrigin),
                    EntryBatchId = null,
                    Comment = correction.Envelope.Comment,
                };
                dbContext.Set<IssuedMembershipRecord>().Add(replacementMembership);
                var paymentWrite = paymentWriter.StageExactSale(
                    correction.Envelope,
                    originalMembership.ClientId,
                    replacementMembership.Id,
                    new Money(
                        replacementMembership.PriceAmountSnapshot,
                        replacementMembership.PriceCurrencySnapshot),
                    entryBatchId: null,
                    recordedAt,
                    changedAfterClose);
                replacementPaymentId = paymentWrite.PaymentId;
                replacementPaymentAuditId = paymentWrite.AuditEntryId;
                replacementPayment = dbContext.Set<PaymentRecord>().Local
                    .Single(payment => payment.Id == paymentWrite.PaymentId);
                originalMembership.Status = "corrected";
                originalPayment.Status = "replaced";
            }
            else
            {
                originalMembership.Status = "canceled";
                originalPayment.Status = "canceled";
            }

            var correctionRecord = new IssuedMembershipSaleCorrectionRecord
            {
                Id = correctionId,
                ClientId = originalMembership.ClientId,
                OriginalMembershipId = originalMembership.Id,
                OriginalPaymentId = originalPayment.Id,
                ReplacementMembershipId = replacementMembershipId,
                ReplacementPaymentId = replacementPaymentId,
                CorrectionMode = IssuedMembershipSaleCorrectionCommandSupport
                    .MapMode(correction.Mode),
                Reason = correction.Envelope.Reason!,
                OccurredAt = correction.Envelope.OccurredAt!.Value,
                RecordedAt = recordedAt,
                RecordedByAccountId = correction.Envelope.Actor.AccountId.Value,
                SessionId = correction.Envelope.Actor.SessionId.Value,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    correction.Envelope.EntryOrigin),
                Status = "active",
                DependencyToken = currentToken,
            };
            dbContext.Set<IssuedMembershipSaleCorrectionRecord>().Add(
                correctionRecord);
            await dbContext.SaveChangesAsync(cancellationToken);

            var originalRebuild = await stateCacheRebuilder.RebuildAsync(
                originalMembership.Id,
                cancellationToken);
            if (!originalRebuild.Succeeded || originalRebuild.State is null)
            {
                return await RollBackAsync(Error(
                    CommandErrorCode.RecalculationFailed,
                    "Original Membership state could not be rebuilt."));
            }

            if (replacementMembershipId is { } newMembershipId)
            {
                var replacementRebuild = await stateCacheRebuilder.RebuildAsync(
                    newMembershipId,
                    cancellationToken);
                if (!replacementRebuild.Succeeded
                    || replacementRebuild.State is null)
                {
                    return await RollBackAsync(Error(
                        CommandErrorCode.RecalculationFailed,
                        "Replacement Membership state could not be rebuilt."));
                }
            }

            var paymentAuditEntryId = AppendPaymentLifecycleAudit(
                correction,
                correctionRecord,
                beforePayment,
                originalPayment,
                replacementPayment,
                recordedAt,
                changedAfterClose);
            var membershipAuditEntryId = auditAppender.Append(
                correction.Envelope,
                correction.Mode == IssuedMembershipSaleCorrectionMode.Replace
                    ? MembershipAuditActions.Replaced
                    : MembershipAuditActions.SaleCanceled,
                MembershipAuditActions.MembershipEntityType,
                originalMembership.Id,
                recordedAt,
                relatedEntityRefs: new
                {
                    correctionRecord.ClientId,
                    SaleCorrectionId = correctionRecord.Id,
                    correctionRecord.OriginalMembershipId,
                    correctionRecord.OriginalPaymentId,
                    correctionRecord.ReplacementMembershipId,
                    correctionRecord.ReplacementPaymentId,
                    PaymentLifecycleAuditEntryId = paymentAuditEntryId.Value,
                    ReplacementPaymentCreatedAuditEntryId =
                        replacementPaymentAuditId?.Value,
                },
                beforeSummary: new
                {
                    OriginalMembership = beforeMembership,
                    OriginalPayment = beforePayment,
                    Dependencies = dependencies.Dependencies,
                },
                afterSummary: new
                {
                    Correction = new
                    {
                        SaleCorrectionId = correctionRecord.Id,
                        Mode = correctionRecord.CorrectionMode,
                        correctionRecord.Reason,
                        correctionRecord.OccurredAt,
                        correctionRecord.RecordedAt,
                        correctionRecord.EntryOrigin,
                        correctionRecord.Status,
                    },
                    OriginalMembership = SummarizeMembership(originalMembership),
                    OriginalPayment = SummarizePayment(originalPayment),
                    ReplacementMembership = replacementMembership is null
                        ? null
                        : SummarizeMembership(replacementMembership),
                    ReplacementPaymentId = replacementPaymentId,
                },
                changedAfterClose: changedAfterClose);

            dbContext.Set<CommandIdempotencyRecord>().Add(
                IssuedMembershipSaleCorrectionCommandSupport
                    .CreateIdempotencyRecord(
                        commandName,
                        correction,
                        recordedAt,
                        correctionRecord,
                        membershipAuditEntryId,
                        fingerprint));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return IssuedMembershipSaleCorrectionCommandSupport.Success(
                correctionRecord,
                membershipAuditEntryId,
                changedAfterClose);
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName is "durationDays" or "startDate")
        {
            return await RollBackAsync(Error(
                CommandErrorCode.ValidationFailed,
                "Replacement start date and duration exceed the supported calendar range.",
                "replacementStartDate"));
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(
                exception);
            if (postgresException is not null
                && IssuedMembershipSaleCorrectionCommandSupport
                    .TryMapPostgresFailure(postgresException, out var result))
            {
                return await RollBackAsync(result);
            }

            await MembershipCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            throw;
        }

        async Task<CommandResult> RollBackAsync(CommandResult result)
        {
            await MembershipCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            return result;
        }
    }

    private AuditEntryId AppendPaymentLifecycleAudit(
        NormalizedIssuedMembershipSaleCorrection correction,
        IssuedMembershipSaleCorrectionRecord source,
        PaymentAuditSummary beforePayment,
        PaymentRecord originalPayment,
        PaymentRecord? replacementPayment,
        DateTimeOffset recordedAt,
        bool changedAfterClose)
    {
        if (correction.Mode == IssuedMembershipSaleCorrectionMode.Replace)
        {
            return auditAppender.Append(
                correction.Envelope,
                PaymentAuditActions.Corrected,
                PaymentAuditActions.EntityType,
                originalPayment.Id,
                recordedAt,
                relatedEntityRefs: new
                {
                    source.ClientId,
                    OriginalPaymentId = originalPayment.Id,
                    OriginalMembershipId = source.OriginalMembershipId,
                    ReplacementPaymentId = replacementPayment?.Id,
                    ReplacementMembershipId = replacementPayment?.MembershipId,
                    CorrectionId = source.Id,
                },
                beforeSummary: new { Payment = beforePayment },
                afterSummary: new
                {
                    Correction = new
                    {
                        CorrectionId = source.Id,
                        source.OriginalPaymentId,
                        ReplacementPaymentId = replacementPayment?.Id,
                        ChangedFields = new[] { "membership_sale" },
                        source.Reason,
                        source.OccurredAt,
                        source.RecordedAt,
                        source.EntryOrigin,
                        EntryBatchId = (Guid?)null,
                        ChangedAfterClose = changedAfterClose,
                    },
                    OriginalPayment = SummarizePayment(originalPayment),
                    ReplacementPayment = SummarizePayment(replacementPayment!),
                },
                changedAfterClose: changedAfterClose);
        }

        return auditAppender.Append(
            correction.Envelope,
            PaymentAuditActions.Canceled,
            PaymentAuditActions.EntityType,
            originalPayment.Id,
            recordedAt,
            relatedEntityRefs: new
            {
                source.ClientId,
                PaymentId = originalPayment.Id,
                MembershipId = source.OriginalMembershipId,
                CancellationId = source.Id,
            },
            beforeSummary: new { Payment = beforePayment },
            afterSummary: new
            {
                Cancellation = new
                {
                    CancellationId = source.Id,
                    PaymentId = originalPayment.Id,
                    source.Reason,
                    source.OccurredAt,
                    source.RecordedAt,
                    source.EntryOrigin,
                    EntryBatchId = (Guid?)null,
                    ChangedAfterClose = changedAfterClose,
                },
                Payment = SummarizePayment(originalPayment),
            },
            changedAfterClose: changedAfterClose);
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
        if (replacementOccurredAt is { } replacement)
        {
            businessDates.Add(BusinessTimeZone.GetBusinessDate(replacement));
        }

        foreach (var businessDate in businessDates)
        {
            var status = await dayReconciliationStatusProvider.GetStatusAsync(
                businessDate,
                cancellationToken);
            if (status == PaymentDayReconciliationStatus.Reconciled)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ClientRecord?> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.clients where id = {clientId} for update")
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<IssuedMembershipRecord?> LockMembershipAsync(
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<IssuedMembershipRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.issued_memberships where id = {membershipId} for update")
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<MembershipTypeRecord?> LockMembershipTypeAsync(
        Guid membershipTypeId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<MembershipTypeRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.membership_types where id = {membershipTypeId} for update")
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private async Task<PaymentRecord[]> LockSalePaymentsAsync(
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Set<PaymentRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.payments where membership_id = {membershipId} and payment_context = 'membership_sale' order by id for update")
            .ToArrayAsync(cancellationToken);
    }

    private async Task LockDependencyRowsAsync(
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        await dbContext.Set<VisitConsumptionRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.visit_consumptions where membership_id = {membershipId} and status = 'active' order by visit_id, id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<VisitRecord>()
            .FromSqlInterpolated(
                $"select visit.* from bodylife.visits visit where exists (select 1 from bodylife.visit_consumptions consumption where consumption.visit_id = visit.id and consumption.membership_id = {membershipId} and consumption.status = 'active') order by visit.occurred_at, visit.id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<FreezeRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.freezes where membership_id = {membershipId} and status = 'active' order by start_date, id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<NonWorkingPeriodApplicationRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.non_working_period_applications where membership_id = {membershipId} and status = 'active' order by non_working_period_id, id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<NonWorkingPeriodRecord>()
            .FromSqlInterpolated(
                $"select period.* from bodylife.non_working_periods period where exists (select 1 from bodylife.non_working_period_applications application where application.non_working_period_id = period.id and application.membership_id = {membershipId} and application.status = 'active') order by period.start_date, period.id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<MembershipNegativeClosureItemRecord>()
            .FromSqlInterpolated(
                $"select * from bodylife.membership_negative_closure_items where status = 'active' and (source_membership_id = {membershipId} or covering_membership_id = {membershipId}) order by visit_id, id for update")
            .LoadAsync(cancellationToken);
        await dbContext.Set<MembershipNegativeClosureRecord>()
            .FromSqlInterpolated(
                $"select closure.* from bodylife.membership_negative_closures closure where exists (select 1 from bodylife.membership_negative_closure_items item where item.negative_closure_id = closure.id and item.status = 'active' and (item.source_membership_id = {membershipId} or item.covering_membership_id = {membershipId})) order by closure.id for update")
            .LoadAsync(cancellationToken);
    }

    private static bool IsEligibleOrdinaryType(MembershipTypeRecord membershipType)
    {
        return membershipType.Kind == "ordinary"
            && !string.IsNullOrWhiteSpace(membershipType.Name)
            && membershipType.DurationDays > 0
            && membershipType.VisitsLimit >= 0
            && membershipType.PriceAmount > 0
            && !string.IsNullOrWhiteSpace(membershipType.PriceCurrency)
            && membershipType.PriceCurrency
                == membershipType.PriceCurrency.Trim().ToUpperInvariant();
    }

    private static MembershipAuditSummary SummarizeMembership(
        IssuedMembershipRecord membership)
    {
        return new MembershipAuditSummary(
            membership.Id,
            membership.ClientId,
            membership.MembershipTypeId,
            membership.TypeNameSnapshot,
            membership.DurationDaysSnapshot,
            membership.VisitsLimitSnapshot,
            membership.PriceAmountSnapshot,
            membership.PriceCurrencySnapshot,
            membership.IssuanceMode,
            membership.StartDate,
            membership.BaseEndDate,
            membership.IssuedAt,
            membership.Status,
            membership.EntryOrigin,
            membership.EntryBatchId,
            membership.Comment);
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
            payment.Status,
            payment.EntryOrigin,
            payment.EntryBatchId,
            payment.Comment);
    }

    private static CommandResult Error(
        CommandErrorCode code,
        string message,
        string? field = null)
    {
        return IssuedMembershipSaleCorrectionCommandSupport.Error(
            code,
            message,
            field);
    }

    private sealed record MembershipAuditSummary(
        Guid MembershipId,
        Guid ClientId,
        Guid MembershipTypeId,
        string TypeNameSnapshot,
        int DurationDaysSnapshot,
        int VisitsLimitSnapshot,
        decimal PriceAmountSnapshot,
        string PriceCurrencySnapshot,
        string IssuanceMode,
        DateOnly StartDate,
        DateOnly BaseEndDate,
        DateTimeOffset IssuedAt,
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);

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
        string Status,
        string EntryOrigin,
        Guid? EntryBatchId,
        string? Comment);
}
