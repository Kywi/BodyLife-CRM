using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Payments;

public sealed record CorrectPaymentCommand(
    CommandEnvelope Envelope,
    Guid OriginalPaymentId,
    PaymentCorrectionMode Mode,
    PaymentReplacement? Replacement,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string CorrectionEntityType = "payment_correction";
    public const string CancellationEntityType = "payment_cancellation";
    public const string PaymentEntityType = "payment";
    public const string CanonicalRereadEntityType = "client";
}
