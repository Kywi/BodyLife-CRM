using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record CreateClientCommand(
    CommandEnvelope Envelope,
    string Surname,
    string Name,
    string? Patronymic,
    string? Phone,
    string? CardNumber,
    string? Comment,
    ClientOperationalStatus OperationalStatus,
    IReadOnlyList<ClientDuplicateWarningAcknowledgement> DuplicateWarningAcknowledgements)
    : IBodyLifeCommand;
