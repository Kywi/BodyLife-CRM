using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record DeactivateMembershipTypeCommand(
    CommandEnvelope Envelope,
    Guid MembershipTypeId,
    DateTimeOffset ExpectedUpdatedAt)
    : IBodyLifeCommand;
