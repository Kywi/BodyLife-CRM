using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record CreatePaymentCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid? MembershipId,
    Money Amount,
    PaymentContext PaymentContext,
    Guid? EntryBatchId = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "payment";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
