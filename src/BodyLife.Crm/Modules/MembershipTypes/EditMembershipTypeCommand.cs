using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record EditMembershipTypeCommand(
    CommandEnvelope Envelope,
    Guid MembershipTypeId,
    DateTimeOffset ExpectedUpdatedAt,
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    string? Comment)
    : IBodyLifeCommand;
