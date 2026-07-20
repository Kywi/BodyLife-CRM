using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record PaymentCancellationHistorySource(
    Guid CancellationId,
    Guid ClientId,
    Guid PaymentId,
    string Reason,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    PaymentHistorySource Payment);
