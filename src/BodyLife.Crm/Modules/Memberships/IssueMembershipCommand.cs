using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record IssueMembershipCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid MembershipTypeId,
    DateOnly StartDate,
    MembershipNegativeHandlingDecision? NegativeHandlingDecision = null,
    Guid? EntryBatchId = null,
    MembershipIssuePayment? Payment = null)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "membership";
    public const string CanonicalRereadEntityType = "client";

    public EntityId CanonicalRereadTargetId =>
        new(CanonicalRereadEntityType, ClientId);
}
