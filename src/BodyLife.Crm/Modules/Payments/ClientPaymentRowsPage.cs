namespace BodyLife.Crm.Modules.Payments;

public sealed record ClientPaymentRowsPage(
    Guid ClientId,
    IReadOnlyList<ClientPaymentRow> Items,
    bool HasMore);
