using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Payments;

public sealed class GetClientPaymentHistorySourceRowsContractsTests
{
    private static readonly DateTimeOffset From = new(
        2026,
        7,
        1,
        0,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void QueryCarriesThePaymentHistorySliceSelectors()
    {
        var actor = CreateActor();
        var clientId = Guid.NewGuid();

        var query = new GetClientPaymentHistorySourceRowsQuery(
            actor,
            clientId,
            From,
            From.AddMonths(1),
            Limit: 25,
            Offset: 50);

        Assert.IsAssignableFrom<
            IBodyLifeQuery<GetClientPaymentHistorySourceRowsResult>>(query);
        Assert.Same(actor, query.Actor);
        Assert.Equal(clientId, query.ClientId);
        Assert.Equal(From, query.OccurredFromInclusive);
        Assert.Equal(From.AddMonths(1), query.OccurredBeforeExclusive);
        Assert.Equal(25, query.Limit);
        Assert.Equal(50, query.Offset);
        Assert.Equal(50, GetClientPaymentHistorySourceRowsQuery.DefaultLimit);
        Assert.Equal(100, GetClientPaymentHistorySourceRowsQuery.MaxLimit);
        Assert.Equal(10_000, GetClientPaymentHistorySourceRowsQuery.MaxOffset);
    }

    [Fact]
    public void PageKeepsCreatedCorrectedAndCanceledFactsAsSeparateImmutableRows()
    {
        var clientId = Guid.NewGuid();
        var original = CreatePayment(clientId, ClientPaymentRowStatus.Replaced);
        var replacement = CreatePayment(clientId, ClientPaymentRowStatus.Active);
        var canceledPayment = CreatePayment(clientId, ClientPaymentRowStatus.Canceled);
        var created = CreateCreatedRow(original);
        var corrected = CreateCorrectedRow(original, replacement);
        var canceled = CreateCanceledRow(canceledPayment);
        var rows = new List<ClientPaymentHistorySourceRow>
        {
            canceled,
            corrected,
            created,
        };

        var page = ClientPaymentHistorySourceRowsPage.Create(
            clientId,
            From,
            From.AddMonths(1),
            offset: 10,
            rows,
            hasMore: true);
        rows.Clear();

        Assert.Equal(
            [
                ClientPaymentHistorySourceKind.CanceledPayment,
                ClientPaymentHistorySourceKind.CorrectedPayment,
                ClientPaymentHistorySourceKind.CreatedPayment,
            ],
            page.Items.Select(row => row.Kind));
        Assert.NotNull(page.Items[0].Cancellation);
        Assert.NotNull(page.Items[1].Correction);
        Assert.NotNull(page.Items[2].CreatedPayment);
        Assert.Equal(
            replacement.PaymentId,
            page.Items[1].Correction!.ReplacementPayment.PaymentId);
        Assert.Equal(13, page.NextOffset);
        Assert.Throws<NotSupportedException>(() =>
            ((ICollection<ClientPaymentHistorySourceRow>)page.Items).Add(created));
        Assert.Throws<ArgumentException>(() =>
            ClientPaymentHistorySourceRowsPage.Create(
                Guid.NewGuid(),
                null,
                null,
                0,
                [created],
                hasMore: false));
    }

    [Fact]
    public void FailureResultsNeverCarryPartialPaymentHistory()
    {
        var failures = new[]
        {
            GetClientPaymentHistorySourceRowsResult.Denied(),
            GetClientPaymentHistorySourceRowsResult.MissingClient(),
            GetClientPaymentHistorySourceRowsResult.Invalid(
                "Invalid range.",
                "occurredBeforeExclusive"),
            GetClientPaymentHistorySourceRowsResult.InconsistentSource(),
        };

        Assert.Equal(
            [
                GetClientPaymentHistorySourceRowsStatus.PermissionDenied,
                GetClientPaymentHistorySourceRowsStatus.NotFound,
                GetClientPaymentHistorySourceRowsStatus.ValidationFailed,
                GetClientPaymentHistorySourceRowsStatus.SourceInconsistent,
            ],
            failures.Select(result => result.Status));
        Assert.All(failures, result =>
        {
            Assert.Null(result.Page);
            Assert.NotNull(result.ErrorCode);
            Assert.NotNull(result.ErrorMessage);
        });
    }

    private static ClientPaymentHistorySourceRow CreateCreatedRow(
        PaymentHistorySource payment)
    {
        var audit = CreateAudit(
            payment.PaymentId,
            "payment.created",
            payment.OccurredAt,
            payment.RecordedAt,
            payment.EntryOrigin,
            reason: null);
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CreatedPayment,
            payment.ClientId,
            payment.PaymentId,
            payment.OccurredAt,
            payment.RecordedAt,
            payment.EntryOrigin,
            payment,
            Correction: null,
            Cancellation: null,
            audit);
    }

    private static ClientPaymentHistorySourceRow CreateCorrectedRow(
        PaymentHistorySource original,
        PaymentHistorySource replacement)
    {
        var occurredAt = From.AddDays(1);
        var recordedAt = occurredAt.AddMinutes(5);
        var audit = CreateAudit(
            original.PaymentId,
            "payment.corrected",
            occurredAt,
            recordedAt,
            EntryOrigin.PaperFallback,
            "Correct cash amount");
        var correction = new PaymentCorrectionHistorySource(
            Guid.NewGuid(),
            original.ClientId,
            original.PaymentId,
            replacement.PaymentId,
            ["amount"],
            "Correct cash amount",
            occurredAt,
            recordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            EntryOrigin.PaperFallback,
            Guid.NewGuid(),
            original,
            replacement);
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CorrectedPayment,
            original.ClientId,
            original.PaymentId,
            occurredAt,
            recordedAt,
            EntryOrigin.PaperFallback,
            CreatedPayment: null,
            correction,
            Cancellation: null,
            audit);
    }

    private static ClientPaymentHistorySourceRow CreateCanceledRow(
        PaymentHistorySource payment)
    {
        var occurredAt = From.AddDays(2);
        var recordedAt = occurredAt.AddMinutes(2);
        var audit = CreateAudit(
            payment.PaymentId,
            "payment.canceled",
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            "Duplicate cash entry");
        var cancellation = new PaymentCancellationHistorySource(
            Guid.NewGuid(),
            payment.ClientId,
            payment.PaymentId,
            "Duplicate cash entry",
            occurredAt,
            recordedAt,
            audit.ActorAccountId,
            audit.SessionId,
            EntryOrigin.ManualBackfill,
            Guid.NewGuid(),
            payment);
        return new ClientPaymentHistorySourceRow(
            ClientPaymentHistorySourceKind.CanceledPayment,
            payment.ClientId,
            payment.PaymentId,
            occurredAt,
            recordedAt,
            EntryOrigin.ManualBackfill,
            CreatedPayment: null,
            Correction: null,
            cancellation,
            audit);
    }

    private static PaymentHistorySource CreatePayment(
        Guid clientId,
        ClientPaymentRowStatus status)
    {
        return new PaymentHistorySource(
            Guid.NewGuid(),
            clientId,
            MembershipId: null,
            MembershipTypeNameSnapshot: null,
            new Money(900m, "UAH"),
            PaymentMethod.Cash,
            PaymentContext.Other,
            From,
            From.AddMinutes(1),
            AccountId.New(),
            SessionId.New(),
            EntryOrigin.Normal,
            EntryBatchId: null,
            Comment: null,
            status,
            CurrentCancellationId: status == ClientPaymentRowStatus.Canceled
                ? Guid.NewGuid()
                : null,
            IncomingCorrectionId: null,
            OutgoingCorrectionId: status == ClientPaymentRowStatus.Replaced
                ? Guid.NewGuid()
                : null);
    }

    private static ClientAuditEntry CreateAudit(
        Guid paymentId,
        string actionType,
        DateTimeOffset occurredAt,
        DateTimeOffset recordedAt,
        EntryOrigin entryOrigin,
        string? reason)
    {
        return new ClientAuditEntry(
            AuditEntryId.New(),
            actionType,
            ClientAuditEntityFilter.Payment,
            paymentId,
            AccountId.New(),
            AccountKind.NamedAdmin,
            ActorRole.Admin,
            SessionId.New(),
            "Reception tablet",
            occurredAt,
            recordedAt,
            entryOrigin,
            reason,
            Comment: null,
            "{}",
            "{}",
            "{}",
            new RequestCorrelationId($"payment-history-{actionType}"),
            $"payment-history-idempotency-{actionType}",
            ChangedAfterClose: false);
    }

    private static ActorContext CreateActor()
    {
        return new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "Reception tablet");
    }
}
