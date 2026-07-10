namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientDuplicateWarningAcknowledgement(
    Guid MatchedClientId,
    ClientDuplicateWarningType WarningType,
    string Reason);
