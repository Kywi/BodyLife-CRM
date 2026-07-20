using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record PaymentHistorySource(
    Guid PaymentId,
    Guid ClientId,
    Guid? MembershipId,
    string? MembershipTypeNameSnapshot,
    Money Amount,
    PaymentMethod Method,
    PaymentContext PaymentContext,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    AccountId RecordedByAccountId,
    SessionId RecordedSessionId,
    EntryOrigin EntryOrigin,
    Guid? EntryBatchId,
    string? Comment,
    ClientPaymentRowStatus CurrentStatus,
    Guid? CurrentCancellationId,
    Guid? IncomingCorrectionId,
    Guid? OutgoingCorrectionId);
