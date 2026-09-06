using System.Data;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;
using BodyLife.Crm.Infrastructure.Persistence.Idempotency;
using BodyLife.Crm.Infrastructure.Persistence.MembershipTypes;
using BodyLife.Crm.Infrastructure.Persistence.Payments;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public sealed class CloseNegativeVisitsOneOffCommandHandler(
    BodyLifeDbContext dbContext,
    BusinessAuditAppender auditAppender,
    PaperFallbackEntryRowBinder paperFallbackEntryRowBinder,
    INegativeClosurePaymentWriter paymentWriter,
    MembershipNegativeVisitSelector negativeVisitSelector,
    MembershipStateCacheRebuilder stateCacheRebuilder,
    TimeProvider timeProvider)
    : IBodyLifeCommandHandler<CloseNegativeVisitsOneOffCommand>
{
    private const string CommandName = "CloseNegativeVisitsOneOff";

    public async Task<CommandResult> ExecuteAsync(
        CloseNegativeVisitsOneOffCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = NegativeCoverageCommandSupport.ValidateAndNormalize(
            command,
            out var normalized);
        if (validation is not null)
        {
            return validation;
        }

        var closure = normalized!;
        if (!MembershipCommandSupport.IsAllowedActorShape(closure.Envelope.Actor))
        {
            return NegativeCoverageCommandSupport.Error(
                CommandErrorCode.PermissionDenied,
                "An active Owner or Admin session is required to close negative Visits.");
        }

        var recordedAt = timeProvider.GetUtcNow();
        var fingerprint = NegativeCoverageCommandSupport.CreateFingerprint(closure);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            if (!await MembershipCommandSupport.IsCanonicalActorAuthorizedAsync(
                    dbContext,
                    closure.Envelope.Actor,
                    recordedAt,
                    cancellationToken))
            {
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.PermissionDenied,
                    "The Owner or Admin account or session is not active.");
            }

            var existingIdempotency = await MembershipCommandSupport
                .FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    closure.IdempotencyKey,
                    cancellationToken);
            if (existingIdempotency is not null)
            {
                return NegativeCoverageCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    closure,
                    fingerprint);
            }

            var client = await LockClientAsync(closure.ClientId, cancellationToken);
            if (client is null)
            {
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.NotFound,
                    "Client was not found.",
                    "clientId");
            }

            existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                dbContext,
                CommandName,
                closure.IdempotencyKey,
                cancellationToken);
            if (existingIdempotency is not null)
            {
                return NegativeCoverageCommandSupport.ReplayOrRejectDuplicate(
                    existingIdempotency,
                    closure,
                    fingerprint);
            }

            var paperBinding = await paperFallbackEntryRowBinder.PrepareAsync(
                closure.Envelope,
                PaperFallbackEventType.NegativeCoverage,
                cancellationToken);
            if (paperBinding.RowAlreadyLinked)
            {
                existingIdempotency = await MembershipCommandSupport.FindIdempotencyAsync(
                    dbContext,
                    CommandName,
                    closure.IdempotencyKey,
                    cancellationToken);
                if (existingIdempotency is not null)
                {
                    return NegativeCoverageCommandSupport.ReplayOrRejectDuplicate(
                        existingIdempotency,
                        closure,
                        fingerprint);
                }
            }

            if (paperBinding.Error is not null)
            {
                return paperBinding.Error;
            }

            var entryBatchId = paperBinding.Reference?.EntryBatchId;

            var preparedLinesResult = await OneOffNegativeClosureLinePreparer
                .PrepareAsync(dbContext, closure.Lines, "lines", cancellationToken);
            if (preparedLinesResult.Error is not null)
            {
                return preparedLinesResult.Error;
            }

            var preparedLines = preparedLinesResult.Preparation!.Lines;
            var selectionResult = await negativeVisitSelector
                .SelectForUpdateAfterClientLockAsync(
                    closure.ClientId,
                    cancellationToken);
            if (selectionResult.Status
                != MembershipNegativeVisitSelectionStatus.Succeeded)
            {
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    selectionResult.Status
                        == MembershipNegativeVisitSelectionStatus.MissingCanonicalState
                        ? "Canonical membership state is missing or stale."
                        : "Canonical membership Visit state is inconsistent.");
            }

            var selection = selectionResult.Selection!;
            if (selection.OldestOpenConcreteVisitId is not { } oldestVisitId)
            {
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.MembershipNotEligible,
                    selection.TotalNegativeBalance > 0
                        ? "Negative balance has no concrete Visit that can be closed."
                        : "Client has no open negative Visits.",
                    "clientId");
            }

            if (oldestVisitId != closure.ExpectedOldestOpenNegativeVisitId)
            {
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.StaleState,
                    "The oldest open negative Visit changed after preview. Refresh canonical state.",
                    "expectedOldestOpenNegativeVisitId");
            }

            if (closure.VisitsCount > selection.OpenConcreteVisits.Count)
            {
                return NegativeCoverageCommandSupport.ValidationError(
                    "Closure quantity cannot exceed the current open concrete negative Visit count.",
                    "lines");
            }

            var closureId = Guid.NewGuid();
            var occurredAt = closure.Envelope.OccurredAt ?? recordedAt;
            var closureRecord = new MembershipNegativeClosureRecord
            {
                Id = closureId,
                ClientId = closure.ClientId,
                ClosureType = "one_off",
                CoveringMembershipId = null,
                OldestOpenNegativeVisitId = oldestVisitId,
                VisitsCount = closure.VisitsCount,
                Comment = closure.Envelope.Comment,
                OccurredAt = occurredAt,
                RecordedAt = recordedAt,
                RecordedByAccountId = closure.Envelope.Actor.AccountId.Value,
                SessionId = closure.Envelope.Actor.SessionId.Value,
                EntryOrigin = MembershipCommandSupport.MapEntryOrigin(
                    closure.Envelope.EntryOrigin),
                EntryBatchId = entryBatchId,
                IdempotencyKey = closure.IdempotencyKey,
                Status = "active",
            };
            dbContext.Set<MembershipNegativeClosureRecord>().Add(closureRecord);
            if (paperBinding.Reference is { } closurePaperReference)
            {
                paperFallbackEntryRowBinder.LinkEntity(
                    closurePaperReference,
                    MembershipNegativeClosureAuditActions.EntityType,
                    closureId);
            }

            var sourceMembershipIds = new HashSet<Guid>();
            var visitIds = new List<Guid>(closure.VisitsCount);
            var lineSummaries = new List<object>(preparedLines.Count);
            var candidateIndex = 0;
            foreach (var preparedLine in preparedLines)
            {
                var lineId = Guid.NewGuid();
                dbContext.Set<MembershipNegativeClosureLineRecord>().Add(
                    new MembershipNegativeClosureLineRecord
                    {
                        Id = lineId,
                        NegativeClosureId = closureId,
                        MembershipTypeId = preparedLine.Record.Id,
                        TypeNameSnapshot = preparedLine.Record.Name,
                        DurationDaysSnapshot = preparedLine.Record.DurationDays,
                        VisitsLimitSnapshot = preparedLine.Record.VisitsLimit,
                        Quantity = preparedLine.Selection.Quantity,
                        UnitPriceAmountSnapshot = preparedLine.Record.PriceAmount,
                        CurrencySnapshot = preparedLine.Record.PriceCurrency,
                        LineTotal = preparedLine.LineTotal,
                        Sequence = preparedLine.Selection.Sequence,
                    });
                if (paperBinding.Reference is { } linePaperReference)
                {
                    paperFallbackEntryRowBinder.LinkEntity(
                        linePaperReference,
                        "membership_negative_closure_line",
                        lineId);
                }
                lineSummaries.Add(new
                {
                    LineId = lineId,
                    preparedLine.Selection.Sequence,
                    MembershipTypeId = preparedLine.Record.Id,
                    TypeName = preparedLine.Record.Name,
                    DurationDays = preparedLine.Record.DurationDays,
                    VisitsLimit = preparedLine.Record.VisitsLimit,
                    preparedLine.Selection.Quantity,
                    UnitPriceAmount = preparedLine.Record.PriceAmount,
                    Currency = preparedLine.Record.PriceCurrency,
                    preparedLine.LineTotal,
                });

                for (var quantity = 0;
                     quantity < preparedLine.Selection.Quantity;
                     quantity++)
                {
                    var candidate = selection.OpenConcreteVisits[candidateIndex];
                    candidateIndex++;
                    sourceMembershipIds.Add(candidate.SourceMembershipId);
                    visitIds.Add(candidate.VisitId);
                    var itemId = Guid.NewGuid();
                    dbContext.Set<MembershipNegativeClosureItemRecord>().Add(
                        new MembershipNegativeClosureItemRecord
                        {
                            Id = itemId,
                            NegativeClosureId = closureId,
                            ClientId = closure.ClientId,
                            ClosureLineId = lineId,
                            Sequence = candidateIndex,
                            VisitId = candidate.VisitId,
                            SourceMembershipId = candidate.SourceMembershipId,
                            OldConsumptionId = candidate.OldConsumptionId,
                            CoveringMembershipId = null,
                            NewConsumptionId = null,
                            Status = "active",
                        });
                    if (paperBinding.Reference is { } itemPaperReference)
                    {
                        paperFallbackEntryRowBinder.LinkEntity(
                            itemPaperReference,
                            "membership_negative_closure_item",
                            itemId);
                    }
                }
            }

            var currency = preparedLines[0].Record.PriceCurrency;
            var totalAmount = preparedLines.Sum(line => line.LineTotal);
            var paymentWrite = paymentWriter.StageExactClosurePayment(
                closure.Envelope,
                closure.ClientId,
                closureId,
                new Money(totalAmount, currency),
                entryBatchId,
                recordedAt,
                paperBinding.Reference);
            if (paperBinding.Reference is { } paymentPaperReference)
            {
                paperFallbackEntryRowBinder.LinkEntity(
                    paymentPaperReference,
                    PaymentAuditActions.EntityType,
                    paymentWrite.PaymentId);
            }
            await dbContext.SaveChangesAsync(cancellationToken);

            Guid? lifecycleClosureId = null;
            Guid? closedMembershipId = null;
            foreach (var membershipId in sourceMembershipIds.Order())
            {
                var rebuild = await stateCacheRebuilder.RebuildAsync(
                    membershipId,
                    cancellationToken);
                if (!rebuild.Succeeded || rebuild.State is null)
                {
                    await MembershipCommandSupport.RollBackAndClearAsync(
                        dbContext,
                        transaction);
                    return NegativeCoverageCommandSupport.Error(
                        CommandErrorCode.RecalculationFailed,
                        "Affected membership state could not be rebuilt from closure facts.");
                }
                if (selection.ActivePredecessor is { } active
                    && active.Id == membershipId && rebuild.State.RemainingVisits == 0)
                {
                    var tracked = dbContext.Set<IssuedMembershipRecord>().Local
                        .SingleOrDefault(row => row.Id == membershipId)
                        ?? dbContext.Attach(active).Entity;
                    tracked.Status = "closed";
                    lifecycleClosureId = Guid.NewGuid();
                    closedMembershipId = membershipId;
                    dbContext.Set<MembershipLifecycleClosureRecord>().Add(new MembershipLifecycleClosureRecord
                    {
                        Id = lifecycleClosureId.Value,
                        ClientId = closure.ClientId,
                        SourceMembershipId = membershipId,
                        NegativeClosureId = closureId,
                        ReasonCode = "one_off_zero_balance",
                        RecordedByAccountId = closure.Envelope.Actor.AccountId.Value,
                        SessionId = closure.Envelope.Actor.SessionId.Value,
                        CorrelationId = closure.Envelope.RequestCorrelationId.Value!,
                        IdempotencyKey = closure.IdempotencyKey,
                        EntryOrigin = MembershipCommandSupport.MapEntryOrigin(closure.Envelope.EntryOrigin),
                        EntryBatchId = entryBatchId,
                        OccurredAt = occurredAt,
                        RecordedAt = recordedAt,
                        Explanation = closure.Envelope.Comment,
                    });
                    if (paperBinding.Reference is { } lifecyclePaper)
                    {
                        paperFallbackEntryRowBinder.LinkEntity(lifecyclePaper,
                            "membership_lifecycle_closure", lifecycleClosureId.Value);
                    }
                }
            }

            var activeMembershipIds = selection.Memberships
                .Select(membership => membership.Id)
                .ToArray();
            var remainingNegativeBalance = await dbContext
                .Set<MembershipStateCacheRecord>()
                .Where(cache => activeMembershipIds.Contains(cache.MembershipId))
                .SumAsync(cache => cache.NegativeBalance, cancellationToken);
            if (remainingNegativeBalance
                != selection.TotalNegativeBalance - closure.VisitsCount)
            {
                await MembershipCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
                return NegativeCoverageCommandSupport.Error(
                    CommandErrorCode.RecalculationFailed,
                    "Closure facts did not produce the expected canonical negative balance.");
            }

            var auditEntryId = auditAppender.Append(
                closure.Envelope,
                MembershipNegativeClosureAuditActions.Created,
                MembershipNegativeClosureAuditActions.EntityType,
                closureId,
                recordedAt,
                relatedEntityRefs: paperBinding.Reference is { } closureAuditPaperReference
                    ? new
                    {
                        ClientId = closure.ClientId,
                        PaymentId = paymentWrite.PaymentId,
                        PaymentAuditEntryId = paymentWrite.AuditEntryId.Value,
                        LifecycleClosureId = lifecycleClosureId,
                        ClosedMembershipId = closedMembershipId,
                        SourceMembershipIds = sourceMembershipIds.Order().ToArray(),
                        VisitIds = visitIds,
                        closureAuditPaperReference.EntryBatchId,
                        closureAuditPaperReference.EntryBatchRowId,
                        closureAuditPaperReference.PaperSheetNumber,
                        closureAuditPaperReference.LineNumber,
                        PaperExplanation = closureAuditPaperReference.Explanation,
                    }
                    : new
                    {
                        ClientId = closure.ClientId,
                        PaymentId = paymentWrite.PaymentId,
                        PaymentAuditEntryId = paymentWrite.AuditEntryId.Value,
                        LifecycleClosureId = lifecycleClosureId,
                        ClosedMembershipId = closedMembershipId,
                        SourceMembershipIds = sourceMembershipIds.Order().ToArray(),
                        VisitIds = visitIds,
                    },
                beforeSummary: new
                {
                    selection.TotalNegativeBalance,
                    OpenConcreteVisitCount = selection.OpenConcreteVisits.Count,
                    selection.UnknownNegativeBalance,
                    OldestOpenNegativeVisitId = oldestVisitId,
                },
                afterSummary: new
                {
                    NegativeClosureId = closureId,
                    LifecycleClosureId = lifecycleClosureId,
                    ClosedMembershipId = closedMembershipId,
                    ClosureReasonCode = lifecycleClosureId.HasValue ? "one_off_zero_balance" : null,
                    ClosureType = closureRecord.ClosureType,
                    closureRecord.VisitsCount,
                    Lines = lineSummaries,
                    Payment = new
                    {
                        paymentWrite.PaymentId,
                        Amount = totalAmount,
                        Currency = currency,
                        Method = "cash",
                        Context = "negative_closure",
                    },
                    CoveredVisitIds = visitIds,
                    RemainingNegativeBalance = remainingNegativeBalance,
                    closureRecord.OccurredAt,
                    closureRecord.RecordedAt,
                    closureRecord.EntryOrigin,
                    closureRecord.EntryBatchId,
                    closureRecord.Status,
                });
            dbContext.Set<CommandIdempotencyRecord>().Add(
                NegativeCoverageCommandSupport.CreateSucceededIdempotencyRecord(
                    CommandName,
                    closure,
                    recordedAt,
                    closureId,
                    auditEntryId,
                    fingerprint));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return NegativeCoverageCommandSupport.Success(
                closureId,
                closure.ClientId,
                paymentWrite.PaymentId,
                sourceMembershipIds,
                auditEntryId,
                remainingNegativeBalance);
        }
        catch (Exception exception)
        {
            var postgresException = MembershipCommandSupport.FindPostgresException(
                exception);
            if (postgresException is null
                || !NegativeCoverageCommandSupport.TryMapPostgresFailure(
                    postgresException,
                    out var error))
            {
                await MembershipCommandSupport.RollBackAndClearAsync(
                    dbContext,
                    transaction);
                throw;
            }

            await MembershipCommandSupport.RollBackAndClearAsync(
                dbContext,
                transaction);
            return error;
        }
    }

    private async Task<ClientRecord?> LockClientAsync(
        Guid clientId,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<ClientRecord>()
            .FromSqlInterpolated(
                $"""
                select *
                from bodylife.clients
                where id = {clientId}
                for no key update
                """)
            .AsNoTracking()
            .ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

}
