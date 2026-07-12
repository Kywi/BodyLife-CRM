using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.MembershipTypes;

public sealed record CreateMembershipTypeCommand(
    CommandEnvelope Envelope,
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    string? Comment,
    bool IsActive = true)
    : IBodyLifeCommand;
