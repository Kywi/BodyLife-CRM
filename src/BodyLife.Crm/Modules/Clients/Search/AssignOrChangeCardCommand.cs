using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record AssignOrChangeCardCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    Guid? ExpectedCurrentCardAssignmentId,
    string? NewCardNumber,
    bool ClearCurrentCard)
    : IBodyLifeCommand;
