namespace BodyLife.Crm.Modules.Clients.Search;

public sealed record ClientProfileCard(
    Guid AssignmentId,
    string CardNumber,
    DateTimeOffset AssignedAt);
