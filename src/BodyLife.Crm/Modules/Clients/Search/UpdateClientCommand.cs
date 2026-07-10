using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record UpdateClientCommand(
    CommandEnvelope Envelope,
    Guid ClientId,
    DateTimeOffset ExpectedUpdatedAt,
    string Surname,
    string Name,
    string? Patronymic,
    string? Phone,
    string? Comment,
    ClientOperationalStatus OperationalStatus,
    IReadOnlyList<ClientDuplicateWarningAcknowledgement> DuplicateWarningAcknowledgements)
    : IBodyLifeCommand;
