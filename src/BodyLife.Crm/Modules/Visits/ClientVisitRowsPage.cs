namespace BodyLife.Crm.Modules.Visits;

public sealed record ClientVisitRowsPage(
    Guid ClientId,
    IReadOnlyList<ClientVisitRow> Items,
    bool HasMore);
